using System.Security.Claims;
using M2Manager.Shared.Dtos;
using Microsoft.AspNetCore.Components.Authorization;

namespace M2Manager.Client.Services;

/// <summary>
/// Stan logowania odczytujemy z /api/auth/me — samego cookie (HttpOnly) przeglądarka
/// nie udostępnia JavaScriptowi, i bardzo dobrze.
/// </summary>
public sealed class CookieAuthenticationStateProvider(ApiClient api) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private AuthenticationState? _cached;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var user = await api.GetCurrentUserAsync();
        _cached = BuildState(user);

        return _cached;
    }

    /// <summary>Wywoływane po zalogowaniu — odświeża cały drzewko komponentów.</summary>
    public void NotifySignedIn(AuthUserDto user)
    {
        _cached = BuildState(user);
        NotifyAuthenticationStateChanged(Task.FromResult(_cached));
    }

    /// <summary>Wywoływane po wylogowaniu albo po odpowiedzi 401 z API.</summary>
    public void NotifySignedOut()
    {
        _cached = Anonymous;
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    private static AuthenticationState BuildState(AuthUserDto? user)
    {
        if (user is null || !user.IsAuthenticated)
        {
            return Anonymous;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Username ?? "użytkownik")],
            authenticationType: "cookie");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
