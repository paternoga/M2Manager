using System.ComponentModel.DataAnnotations;

namespace M2Manager.Shared.Dtos;

/// <summary>Uniwersalna strona wyników.</summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }

    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;
}

/// <summary>Prosty słownik (id + nazwa) — kategorie faktur i zakupów.</summary>
public sealed class LookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class LookupUpsertDto
{
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Sklep albo wykonawca — słownik podpowiadany przy fakturach.</summary>
public sealed class ShopDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ShopUpsertDto
{
    [Required(ErrorMessage = "Nazwa sklepu jest wymagana.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Url { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Dane logowania na wspólne konto.</summary>
public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Odpowiedź /api/auth/me.</summary>
public sealed class AuthUserDto
{
    public bool IsAuthenticated { get; set; }
    public string? Username { get; set; }
}
