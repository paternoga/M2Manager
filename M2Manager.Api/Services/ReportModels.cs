using M2Manager.Shared.Dtos;

namespace M2Manager.Api.Services;

/// <summary>Pojedynczy wiersz zestawienia kosztów.</summary>
public sealed record InvoiceReportRow(
    DateOnly? IssueDate,
    string? Vendor,
    string? Category,
    string? Room,
    string? Payer,
    string? Description,
    decimal? Amount,
    string Currency);

/// <summary>Komplet danych do PDF-a i Excela z fakturami.</summary>
public sealed class InvoiceReportData
{
    public string PropertyName { get; init; } = string.Empty;
    public string PeriodLabel { get; init; } = string.Empty;
    public string Currency { get; init; } = "PLN";
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;

    public List<InvoiceReportRow> Rows { get; init; } = [];
    public List<CategoryTotalDto> ByCategory { get; init; } = [];
    public List<MonthTotalDto> ByMonth { get; init; } = [];
    public List<PayerTotalDto> ByPayer { get; init; } = [];

    public decimal Total { get; init; }
    public int MissingAmountCount { get; init; }
}

/// <summary>Komplet danych do eksportu listy zakupów.</summary>
public sealed class ShoppingReportData
{
    public string PropertyName { get; init; } = string.Empty;
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public List<ShoppingItemDto> Items { get; init; } = [];
    public ShoppingSummaryDto Summary { get; init; } = new();
}
