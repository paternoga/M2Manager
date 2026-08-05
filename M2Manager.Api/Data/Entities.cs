using M2Manager.Shared;
using M2Manager.Shared.Areas;

namespace M2Manager.Api.Data;

/// <summary>Mieszkanie.</summary>
public class Property
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public PropertyPurpose Purpose { get; set; } = PropertyPurpose.OwnOccupied;
    public decimal? TotalAreaM2 { get; set; }

    /// <summary>Domyślna wysokość pomieszczeń — używana, gdy pokój nie ma własnej.</summary>
    public decimal DefaultRoomHeightM { get; set; } = AreaCalculator.FallbackRoomHeightM;

    public ICollection<Room> Rooms { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<ShoppingItem> ShoppingItems { get; set; } = [];
}

/// <summary>Pomieszczenie. Implementuje kontrakt kalkulatora, więc liczy się tym samym kodem co UI.</summary>
public class Room : IRoomAreaSource
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public decimal? FloorAreaM2 { get; set; }
    public decimal? LengthM { get; set; }
    public decimal? WidthM { get; set; }
    public decimal? HeightM { get; set; }
    public decimal? ManualWallAreaM2 { get; set; }
    public decimal? ExcludedWallAreaM2 { get; set; }
    public decimal? ManualCeilingAreaM2 { get; set; }
    public bool IncludeInTotals { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>Geometria na rzucie (JSON, wartości w cm).</summary>
    public string? GeometryJson { get; set; }

    public ICollection<RoomOpening> Openings { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<ShoppingItem> ShoppingItems { get; set; } = [];
}

/// <summary>Okno albo drzwi — powierzchnia odejmowana od ścian.</summary>
public class RoomOpening : IOpeningAreaSource
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }

    public OpeningType Type { get; set; } = OpeningType.Window;
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public int Count { get; set; } = 1;
    public bool SubtractFromWalls { get; set; } = true;

    public WallSide? WallSide { get; set; }
    public decimal? OffsetCm { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Kategoria wydatku (dotyczy faktur).</summary>
public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = [];
}

/// <summary>Faktura albo paragon wraz ze zdjęciem w R2.</summary>
public class Invoice
{
    public int Id { get; set; }

    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    public int? ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }

    public string? Vendor { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateOnly? IssueDate { get; set; }
    public string? Description { get; set; }

    /// <summary>Klucz obiektu w Cloudflare R2.</summary>
    public string ImageObjectKey { get; set; } = string.Empty;

    public OcrStatus OcrStatus { get; set; } = OcrStatus.Pending;
    public string? OcrRawResponse { get; set; }

    /// <summary>
    /// Pozycje odczytane z faktury (JSON). Trzymamy je przy dokumencie, żeby dało się
    /// wrócić do faktury później i dopiero wtedy przenieść wybrane pozycje na listę zakupów.
    /// </summary>
    public string? OcrLineItemsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ShoppingItem> ShoppingItems { get; set; } = [];
}

/// <summary>Kategoria listy zakupów — słownik niezależny od kategorii faktur.</summary>
public class ShoppingCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<ShoppingItem> Items { get; set; } = [];
}

/// <summary>Pozycja listy zakupów/remontu.</summary>
public class ShoppingItem
{
    public int Id { get; set; }

    /// <summary>L.p — numeracja w obrębie mieszkania.</summary>
    public int OrdinalNo { get; set; }

    public int PropertyId { get; set; }
    public Property? Property { get; set; }

    /// <summary>Puste = „Całe mieszkanie”.</summary>
    public int? RoomId { get; set; }

    public Room? Room { get; set; }

    public int? ShoppingCategoryId { get; set; }
    public ShoppingCategory? ShoppingCategory { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Uwagi/obliczenia — tu lądują wyniki kalkulatora materiałów.</summary>
    public string? CalculationNotes { get; set; }

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal? PlannedBudget { get; set; }
    public decimal? ActualCost { get; set; }

    public string? Vendor { get; set; }
    public string? Link { get; set; }

    public ShoppingStatus Status { get; set; } = ShoppingStatus.ToBuy;
    public ShoppingPriority Priority { get; set; } = ShoppingPriority.MustHave;

    public DateOnly? PurchaseDate { get; set; }

    /// <summary>Powiązanie z fakturą z modułu 1.</summary>
    public int? InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
