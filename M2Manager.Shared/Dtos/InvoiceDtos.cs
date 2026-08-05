using System.ComponentModel.DataAnnotations;

namespace M2Manager.Shared.Dtos;

public sealed class InvoiceDto
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int? RoomId { get; set; }
    public string? RoomName { get; set; }
    public int? ExpenseCategoryId { get; set; }
    public string? ExpenseCategoryName { get; set; }

    /// <summary>Kto pokrył ten wydatek — podstawa podziału kosztów.</summary>
    public int? PayerId { get; set; }

    public string? PayerName { get; set; }

    public string? Vendor { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateOnly? IssueDate { get; set; }
    public string? Description { get; set; }
    public string ImageObjectKey { get; set; } = string.Empty;

    /// <summary>Presigned URL do podglądu zdjęcia (bucket jest prywatny).</summary>
    public string? ImageUrl { get; set; }

    public OcrStatus OcrStatus { get; set; }
    public string? OcrRawResponse { get; set; }

    /// <summary>Pozycje odczytane z faktury, gotowe do przeniesienia na listę zakupów.</summary>
    public List<OcrLineItemDto> LineItems { get; set; } = [];

    /// <summary>Ile pozycji z tej faktury trafiło już na listę zakupów.</summary>
    public int LinkedShoppingItemsCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Żądanie przeniesienia wybranych pozycji faktury na listę zakupów.</summary>
public sealed class CreateShoppingItemsFromInvoiceRequest
{
    /// <summary>Pomieszczenie dla tworzonych pozycji; puste = jak na fakturze albo „Całe mieszkanie”.</summary>
    public int? RoomId { get; set; }

    /// <summary>Status tworzonych pozycji — domyślnie „Kupione”, bo faktura już jest.</summary>
    public ShoppingStatus Status { get; set; } = ShoppingStatus.Bought;

    public List<OcrLineItemDto> Items { get; set; } = [];
}

public sealed class InvoiceUpsertDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Wybierz mieszkanie.")]
    public int PropertyId { get; set; }

    public int? RoomId { get; set; }
    public int? ExpenseCategoryId { get; set; }
    public int? PayerId { get; set; }

    [StringLength(300)]
    public string? Vendor { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Kwota musi być dodatnia.")]
    public decimal? Amount { get; set; }

    [StringLength(3, MinimumLength = 3, ErrorMessage = "Waluta to trzyliterowy kod, np. PLN.")]
    public string Currency { get; set; } = "PLN";

    public DateOnly? IssueDate { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Zapis formularza po weryfikacji człowieka ustawia status na Confirmed.</summary>
    public bool MarkConfirmed { get; set; } = true;
}

/// <summary>
/// Pojedyncza pozycja odczytana z faktury — np. „Klej do płytek 25 kg”.
/// Każda z nich może trafić na listę zakupów jako osobny wiersz.
/// </summary>
public sealed class OcrLineItemDto
{
    public string Name { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalPrice { get; set; }

    /// <summary>Podpowiedź kategorii z listy zakupów (Płytki, Ściany, AGD…).</summary>
    public string? SuggestedCategoryName { get; set; }
}

/// <summary>Słowniki przekazywane modelowi, żeby podpowiadał istniejące kategorie zamiast wymyślać własne.</summary>
public sealed record OcrCategories(
    IReadOnlyCollection<string> Expense,
    IReadOnlyCollection<string> Shopping)
{
    public static OcrCategories Empty { get; } = new([], []);
}

/// <summary>Wynik odczytu faktury przez AI — propozycja do korekty, nie prawda ostateczna.</summary>
public sealed class OcrExtractionResult
{
    public bool Success { get; set; }
    public string? Vendor { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? IssueDate { get; set; }
    public string? SuggestedCategoryName { get; set; }

    /// <summary>Pozycje z faktury — pusta lista, gdy dokument ma tylko sumę.</summary>
    public List<OcrLineItemDto> LineItems { get; set; } = [];

    /// <summary>Surowa odpowiedź modelu — zostaje w bazie do debugowania.</summary>
    public string? RawResponse { get; set; }

    public string? Error { get; set; }

    public static OcrExtractionResult Failed(string error, string? raw = null) => new()
    {
        Success = false,
        Error = error,
        RawResponse = raw
    };
}

/// <summary>Filtry listy faktur.</summary>
public sealed class InvoiceQuery
{
    public int? PropertyId { get; set; }
    public int? RoomId { get; set; }
    public int? CategoryId { get; set; }
    public int? PayerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
