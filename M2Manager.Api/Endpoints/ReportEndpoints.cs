using M2Manager.Api.Data;
using M2Manager.Api.Services;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Endpoints;

/// <summary>Raporty kosztów: podsumowanie na ekran, PDF do banku i .xlsx dla księgowej.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").RequireAuthorization();

        group.MapGet("/summary", async (
            int propertyId,
            int? year,
            int? month,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId, ct);
            if (property is null)
            {
                return Results.NotFound();
            }

            var summary = await BuildSummaryAsync(db, property, year ?? DateTime.UtcNow.Year, month, ct);
            return Results.Ok(summary);
        });

        group.MapGet("/export/pdf", async (
            int propertyId,
            int? year,
            int? month,
            AppDbContext db,
            PdfExportService pdf,
            CancellationToken ct) =>
        {
            var data = await BuildReportDataAsync(db, propertyId, year ?? DateTime.UtcNow.Year, month, ct);
            if (data is null)
            {
                return Results.NotFound();
            }

            var bytes = pdf.BuildInvoiceReport(data);

            return Results.File(
                bytes,
                "application/pdf",
                ShoppingEndpoints.BuildFileName("zestawienie-kosztow", data.PropertyName, "pdf"));
        });

        group.MapGet("/export/excel", async (
            int propertyId,
            int? year,
            int? month,
            AppDbContext db,
            ExcelExportService excel,
            CancellationToken ct) =>
        {
            var data = await BuildReportDataAsync(db, propertyId, year ?? DateTime.UtcNow.Year, month, ct);
            if (data is null)
            {
                return Results.NotFound();
            }

            var bytes = excel.BuildInvoiceReport(data);

            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ShoppingEndpoints.BuildFileName("zestawienie-kosztow", data.PropertyName, "xlsx"));
        });

        // Kafelki na stronę główną.
        group.MapGet("/dashboard", async (AppDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var properties = await db.Properties
                .Include(p => p.Rooms)
                .ThenInclude(r => r.Openings)
                .OrderBy(p => p.Id)
                .AsNoTracking()
                .ToListAsync(ct);

            var tiles = new List<DashboardTileDto>();

            foreach (var property in properties)
            {
                var invoices = await db.Invoices
                    .Where(i => i.PropertyId == property.Id)
                    .Select(i => new { i.Amount, i.IssueDate, i.CreatedAt })
                    .AsNoTracking()
                    .ToListAsync(ct);

                // Faktura bez daty wystawienia liczy się według daty dodania.
                DateOnly EffectiveDate(DateOnly? issueDate, DateTime createdAt) =>
                    issueDate ?? DateOnly.FromDateTime(createdAt);

                var yearTotal = invoices
                    .Where(i => EffectiveDate(i.IssueDate, i.CreatedAt).Year == now.Year)
                    .Sum(i => i.Amount ?? 0m);

                var monthTotal = invoices
                    .Where(i =>
                    {
                        var date = EffectiveDate(i.IssueDate, i.CreatedAt);
                        return date.Year == now.Year && date.Month == now.Month;
                    })
                    .Sum(i => i.Amount ?? 0m);

                var shoppingItems = await db.ShoppingItems
                    .Where(i => i.PropertyId == property.Id)
                    .Include(i => i.Room)
                    .Include(i => i.ShoppingCategory)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var shoppingSummary = ShoppingSummaryBuilder.Build(
                    property.Id,
                    shoppingItems.Select(i => i.ToDto()).ToList());

                var areas = PropertyEndpoints.BuildAreas(property);

                tiles.Add(new DashboardTileDto
                {
                    PropertyId = property.Id,
                    PropertyName = property.Name,
                    Purpose = property.Purpose,
                    CurrentMonthTotal = Math.Round(monthTotal, 2, MidpointRounding.AwayFromZero),
                    CurrentYearTotal = Math.Round(yearTotal, 2, MidpointRounding.AwayFromZero),
                    InvoicesCount = invoices.Count,
                    ShoppingItemsCount = shoppingSummary.ItemsCount,
                    ShoppingDoneCount = shoppingSummary.DoneCount,
                    ShoppingProgressPercent = shoppingSummary.ProgressPercent,
                    ShoppingPlannedBudget = shoppingSummary.PlannedBudget,
                    ShoppingActualCost = shoppingSummary.ActualCost,
                    ShoppingBudgetDifference = shoppingSummary.BudgetDifference,
                    TotalWallsAndCeilingM2 = areas.Summary.TotalWallsAndCeilingM2
                });
            }

            return Results.Ok(new DashboardDto
            {
                Year = now.Year,
                Month = now.Month,
                PeriodLabel = Formatting.PeriodLabel(now.Year, now.Month),
                Tiles = tiles
            });
        });
    }

    // ---------------------------------------------------------------- budowanie danych

    private static async Task<ReportSummaryDto> BuildSummaryAsync(
        AppDbContext db,
        Property property,
        int year,
        int? month,
        CancellationToken ct)
    {
        var invoices = await LoadInvoicesAsync(db, property.Id, year, month, ct);

        var byCategory = invoices
            .GroupBy(i => (i.ExpenseCategoryId, Name: i.ExpenseCategory?.Name ?? "Bez kategorii"))
            .Select(g => new CategoryTotalDto
            {
                CategoryId = g.Key.ExpenseCategoryId,
                CategoryName = g.Key.Name,
                InvoicesCount = g.Count(),
                Total = Round(g.Sum(i => i.Amount ?? 0m))
            })
            .OrderByDescending(c => c.Total)
            .ThenBy(c => c.CategoryName, StringComparer.CurrentCulture)
            .ToList();

        var byMonth = invoices
            .GroupBy(i => EffectiveDate(i).Month)
            .Select(g => new MonthTotalDto
            {
                Year = year,
                Month = g.Key,
                MonthName = Formatting.MonthName(g.Key),
                InvoicesCount = g.Count(),
                Total = Round(g.Sum(i => i.Amount ?? 0m))
            })
            .OrderBy(m => m.Month)
            .ToList();

        return new ReportSummaryDto
        {
            PropertyId = property.Id,
            PropertyName = property.Name,
            Year = year,
            Month = month,
            PeriodLabel = Formatting.PeriodLabel(year, month),
            Currency = invoices.FirstOrDefault()?.Currency ?? "PLN",
            InvoicesCount = invoices.Count,
            Total = Round(invoices.Sum(i => i.Amount ?? 0m)),
            MissingAmountCount = invoices.Count(i => i.Amount is null),
            ByCategory = byCategory,
            ByMonth = byMonth
        };
    }

    private static async Task<InvoiceReportData?> BuildReportDataAsync(
        AppDbContext db,
        int propertyId,
        int year,
        int? month,
        CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null)
        {
            return null;
        }

        var invoices = await LoadInvoicesAsync(db, propertyId, year, month, ct);
        var summary = await BuildSummaryAsync(db, property, year, month, ct);

        var rows = invoices
            .OrderBy(i => EffectiveDate(i))
            .ThenBy(i => i.Id)
            .Select(i => new InvoiceReportRow(
                i.IssueDate,
                i.Vendor,
                i.ExpenseCategory?.Name,
                i.Room?.Name,
                i.Description,
                i.Amount,
                i.Currency))
            .ToList();

        return new InvoiceReportData
        {
            PropertyName = property.Name,
            PeriodLabel = Formatting.PeriodLabel(year, month),
            Currency = summary.Currency,
            Rows = rows,
            ByCategory = summary.ByCategory,
            ByMonth = summary.ByMonth,
            Total = summary.Total,
            MissingAmountCount = summary.MissingAmountCount
        };
    }

    /// <summary>
    /// Faktury z danego okresu. Dokument bez daty wystawienia przypisujemy do daty dodania —
    /// inaczej wypadałby z każdego raportu i suma nie zgadzałaby się z rzeczywistością.
    /// </summary>
    private static async Task<List<Invoice>> LoadInvoicesAsync(
        AppDbContext db,
        int propertyId,
        int year,
        int? month,
        CancellationToken ct)
    {
        var invoices = await db.Invoices
            .Where(i => i.PropertyId == propertyId)
            .Include(i => i.ExpenseCategory)
            .Include(i => i.Room)
            .AsNoTracking()
            .ToListAsync(ct);

        return invoices
            .Where(i =>
            {
                var date = EffectiveDate(i);
                return date.Year == year && (!month.HasValue || date.Month == month.Value);
            })
            .ToList();
    }

    private static DateOnly EffectiveDate(Invoice invoice) =>
        invoice.IssueDate ?? DateOnly.FromDateTime(invoice.CreatedAt);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
