namespace M2Manager.Shared.Dtos;

public sealed class CategoryTotalDto
{
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = "Bez kategorii";
    public int InvoicesCount { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Ile kosztów pokryła dana osoba — podstawa rozliczenia między domownikami.</summary>
public sealed class PayerTotalDto
{
    public int? PayerId { get; set; }
    public string PayerName { get; set; } = "Nieprzypisane";
    public int InvoicesCount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Udział procentowy w sumie okresu.</summary>
    public decimal SharePercent { get; set; }
}

public sealed class MonthTotalDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public int InvoicesCount { get; set; }
    public decimal Total { get; set; }
}

public sealed class ReportSummaryDto
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int? Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public string Currency { get; set; } = "PLN";
    public int InvoicesCount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Liczba faktur bez wpisanej kwoty — sygnał, że raport jest jeszcze niekompletny.</summary>
    public int MissingAmountCount { get; set; }

    public List<CategoryTotalDto> ByCategory { get; set; } = [];
    public List<MonthTotalDto> ByMonth { get; set; } = [];

    /// <summary>Podział kosztów — kto ile pokrył w tym okresie.</summary>
    public List<PayerTotalDto> ByPayer { get; set; } = [];
}

/// <summary>Kafelek mieszkania na dashboardzie.</summary>
public sealed class DashboardTileDto
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public PropertyPurpose Purpose { get; set; }
    public decimal CurrentMonthTotal { get; set; }
    public decimal CurrentYearTotal { get; set; }
    public int InvoicesCount { get; set; }
    public int ShoppingItemsCount { get; set; }
    public int ShoppingDoneCount { get; set; }
    public decimal ShoppingProgressPercent { get; set; }
    public decimal ShoppingPlannedBudget { get; set; }
    public decimal ShoppingActualCost { get; set; }
    public decimal ShoppingBudgetDifference { get; set; }
    public decimal TotalWallsAndCeilingM2 { get; set; }
}

public sealed class DashboardDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public List<DashboardTileDto> Tiles { get; set; } = [];
}
