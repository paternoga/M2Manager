using System.Security.Claims;
using M2Manager.Api.Configuration;
using M2Manager.Api.Services;
using M2Manager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Endpoints;

/// <summary>
/// Jedno wspólne konto dla dwóch osób. Bez rejestracji, bez ról —
/// sesja trzymana w cookie HttpOnly (przy Blazor WASM Hosted to najprostsze i najbezpieczniejsze).
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IOptions<AuthOptions> authOptions,
            HttpContext http,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Auth");
            var options = authOptions.Value;

            if (!IsValidLogin(request, options))
            {
                logger.LogWarning("Nieudana próba logowania dla użytkownika {User}.", request.Username);
                return Results.Problem(
                    title: "Nieprawidłowy login lub hasło.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, options.Username)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(Math.Max(1, options.SessionDays))
                });

            return Results.Ok(new AuthUserDto { IsAuthenticated = true, Username = options.Username });
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new AuthUserDto { IsAuthenticated = false });
        });

        group.MapGet("/me", (HttpContext http) =>
        {
            var isAuthenticated = http.User.Identity?.IsAuthenticated == true;

            return Results.Ok(new AuthUserDto
            {
                IsAuthenticated = isAuthenticated,
                Username = isAuthenticated ? http.User.Identity!.Name : null
            });
        });
    }

    private static bool IsValidLogin(LoginRequest request, AuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return false;
        }

        if (!string.Equals(request.Username.Trim(), options.Username, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Hash ma pierwszeństwo; hasło jawne to wygoda przy pierwszym uruchomieniu lokalnym.
        if (!string.IsNullOrWhiteSpace(options.PasswordHash))
        {
            return PasswordHasher.Verify(request.Password, options.PasswordHash);
        }

        return !string.IsNullOrWhiteSpace(options.Password)
               && PasswordHasher.VerifyPlainText(request.Password, options.Password);
    }
}
