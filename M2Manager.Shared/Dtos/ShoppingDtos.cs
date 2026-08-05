using System.ComponentModel.DataAnnotations;

namespace M2Manager.Shared.Dtos;

public sealed class ShoppingItemDto
{
    public int Id { get; set; }
    public int OrdinalNo { get; set; }
    public int PropertyId { get; set; }
    public int? RoomId { get; set; }

    /// <summary>Nazwa pomieszczenia albo „Całe mieszkanie”, gdy RoomId jest puste.</summary>
    public string RoomName { get; set; } = ShoppingConstants.WholePropertyRoomName;

    public int? ShoppingCategoryId { get; set; }
    public string? CategoryName { get; set; }

    /// <summary>Kto finansuje tę pozycję — niezależne od AssignedTo, czyli od tego, kto ją kupuje.</summary>
    public int? PayerId { get; set; }

    public string? PayerName { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
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
    public int? InvoiceId { get; set; }
    public string? InvoiceLabel { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ShoppingItemUpsertDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Wybierz mieszkanie.")]
    public int PropertyId { get; set; }

    public int? RoomId { get; set; }
    public int? ShoppingCategoryId { get; set; }
    public int? PayerId { get; set; }

    [Required(ErrorMessage = "Nazwa pozycji jest wymagana.")]
    [StringLength(300)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? CalculationNotes { get; set; }

    [Range(0, 1_000_000)]
    public decimal? Quantity { get; set; }

    [StringLength(20)]
    public string? Unit { get; set; }

    [Range(0, 10_000_000)]
    public decimal? UnitCost { get; set; }

    /// <summary>Puste = policz automatycznie jako Ilość × Koszt szt.</summary>
    [Range(0, 100_000_000)]
    public decimal? TotalCost { get; set; }

    [Range(0, 100_000_000)]
    public decimal? PlannedBudget { get; set; }

    [Range(0, 100_000_000)]
    public decimal? ActualCost { get; set; }

    [StringLength(300)]
    public string? Vendor { get; set; }

    [StringLength(1000)]
    public string? Link { get; set; }

    public ShoppingStatus Status { get; set; } = ShoppingStatus.ToBuy;
    public ShoppingPriority Priority { get; set; } = ShoppingPriority.MustHave;
    public DateOnly? PurchaseDate { get; set; }
    public int? InvoiceId { get; set; }

    [StringLength(100)]
    public string? AssignedTo { get; set; }

    /// <summary>Ustawiane tylko przy dodawaniu z kalkulatora materiałów; puste = numeracja automatyczna.</summary>
    public int? OrdinalNo { get; set; }
}

/// <summary>Wiersz podsumowania (per pomieszczenie / per kategoria / per status).</summary>
public sealed class ShoppingGroupTotalDto
{
    public string Key { get; set; } = string.Empty;
    public int? Id { get; set; }
    public int ItemsCount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal PlannedBudget { get; set; }
    public decimal ActualCost { get; set; }
}

public sealed class ShoppingSummaryDto
{
    public int PropertyId { get; set; }
    public int ItemsCount { get; set; }
    public int DoneCount { get; set; }

    /// <summary>Procent pozycji ze statusem Kupione/Zamontowane (bez pozycji porzuconych).</summary>
    public decimal ProgressPercent { get; set; }

    public decimal TotalCost { get; set; }
    public decimal PlannedBudget { get; set; }
    public decimal ActualCost { get; set; }

    /// <summary>Planowany budżet − rzeczywisty koszt. Dodatnia wartość = mieścimy się w budżecie.</summary>
    public decimal BudgetDifference { get; set; }

    public List<ShoppingGroupTotalDto> ByRoom { get; set; } = [];
    public List<ShoppingGroupTotalDto> ByCategory { get; set; } = [];
    public List<ShoppingGroupTotalDto> ByStatus { get; set; } = [];

    /// <summary>Podział kosztów — ile faktycznie włożyła każda z osób.</summary>
    public List<ShoppingGroupTotalDto> ByPayer { get; set; } = [];
}

/// <summary>Wynik importu arkusza .xlsx.</summary>
public sealed class ShoppingImportResultDto
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> CreatedRooms { get; set; } = [];
    public List<string> CreatedCategories { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public static class ShoppingConstants
{
    /// <summary>Wartość w kolumnie „Pomieszczenie” oznaczająca pozycję nieprzypisaną do pokoju.</summary>
    public const string WholePropertyRoomName = "Całe mieszkanie";

    /// <summary>
    /// Wartownik w filtrach: „pokaż tylko pozycje bez przypisanego pomieszczenia”.
    /// Zwykłe pominięcie parametru znaczy „wszystkie pomieszczenia”, więc potrzebna jest
    /// osobna wartość — żadne pomieszczenie nie ma Id = 0.
    /// </summary>
    public const int WholePropertyRoomId = 0;
}
