using ClosedXML.Excel;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;

namespace M2Manager.Api.Services;

/// <summary>Eksporty .xlsx (ClosedXML). Formaty liczbowe są „prawdziwe”, żeby dało się w Excelu dalej liczyć.</summary>
public sealed class ExcelExportService
{
    private const string MoneyFormat = "# ##0.00";
    private const string DateFormat = "dd.mm.yyyy";
    private const string HeaderColor = "#1F4E79";

    // ================================================================ faktury

    public byte[] BuildInvoiceReport(InvoiceReportData data)
    {
        using var workbook = new XLWorkbook();

        BuildInvoiceItemsSheet(workbook, data);
        BuildInvoiceSummarySheet(workbook, data);

        return Save(workbook);
    }

    private static void BuildInvoiceItemsSheet(XLWorkbook workbook, InvoiceReportData data)
    {
        var ws = workbook.Worksheets.Add("Faktury");

        ws.Cell(1, 1).Value = "Zestawienie kosztów";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"{data.PropertyName} · okres: {data.PeriodLabel}";
        ws.Cell(3, 1).Value = $"Wygenerowano: {Formatting.DateTimeLocal(data.GeneratedAtUtc)}";

        const int headerRow = 5;
        string[] headers = ["Data", "Sprzedawca", "Kategoria", "Pomieszczenie", "Finansuje", "Opis", "Kwota", "Waluta"];

        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(headerRow, i + 1).Value = headers[i];
        }

        StyleHeader(ws.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var item in data.Rows)
        {
            SetDate(ws.Cell(row, 1), item.IssueDate);
            ws.Cell(row, 2).Value = item.Vendor ?? string.Empty;
            ws.Cell(row, 3).Value = item.Category ?? string.Empty;
            ws.Cell(row, 4).Value = item.Room ?? string.Empty;
            ws.Cell(row, 5).Value = item.Payer ?? string.Empty;
            ws.Cell(row, 6).Value = item.Description ?? string.Empty;
            SetMoney(ws.Cell(row, 7), item.Amount);
            ws.Cell(row, 8).Value = item.Currency;
            row++;
        }

        // Wiersz sumy — jako formuła, żeby zgadzał się nawet po ręcznej edycji arkusza.
        if (data.Rows.Count > 0)
        {
            ws.Cell(row, 6).Value = "Razem";
            ws.Cell(row, 6).Style.Font.SetBold();
            ws.Cell(row, 7).FormulaA1 = $"SUM(G{headerRow + 1}:G{row - 1})";
            ws.Cell(row, 7).Style.NumberFormat.Format = MoneyFormat;
            ws.Cell(row, 7).Style.Font.SetBold();
            ws.Cell(row, 8).Value = data.Currency;
            ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();
        ws.Column(6).Width = Math.Min(ws.Column(6).Width, 45);
    }

    private static void BuildInvoiceSummarySheet(XLWorkbook workbook, InvoiceReportData data)
    {
        var ws = workbook.Worksheets.Add("Podsumowanie");

        ws.Cell(1, 1).Value = "Podsumowanie według kategorii";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);

        ws.Cell(2, 1).Value = "Kategoria";
        ws.Cell(2, 2).Value = "Dokumentów";
        ws.Cell(2, 3).Value = "Kwota";
        StyleHeader(ws.Range(2, 1, 2, 3));

        var row = 3;
        foreach (var category in data.ByCategory)
        {
            ws.Cell(row, 1).Value = category.CategoryName;
            ws.Cell(row, 2).Value = category.InvoicesCount;
            SetMoney(ws.Cell(row, 3), category.Total);
            row++;
        }

        ws.Cell(row, 1).Value = "Razem";
        ws.Cell(row, 1).Style.Font.SetBold();
        SetMoney(ws.Cell(row, 3), data.Total);
        ws.Cell(row, 3).Style.Font.SetBold();
        ws.Range(row, 1, row, 3).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        // ---- podział kosztów między osoby ----
        var payerStart = row + 3;
        ws.Cell(payerStart, 1).Value = "Podział kosztów";
        ws.Cell(payerStart, 1).Style.Font.SetBold().Font.SetFontSize(12);

        ws.Cell(payerStart + 1, 1).Value = "Kto finansuje";
        ws.Cell(payerStart + 1, 2).Value = "Dokumentów";
        ws.Cell(payerStart + 1, 3).Value = "Kwota";
        ws.Cell(payerStart + 1, 4).Value = "Udział";
        StyleHeader(ws.Range(payerStart + 1, 1, payerStart + 1, 4));

        var payerRow = payerStart + 2;
        foreach (var payer in data.ByPayer)
        {
            ws.Cell(payerRow, 1).Value = payer.PayerName;
            ws.Cell(payerRow, 2).Value = payer.InvoicesCount;
            SetMoney(ws.Cell(payerRow, 3), payer.Total);
            ws.Cell(payerRow, 4).Value = payer.SharePercent / 100m;
            ws.Cell(payerRow, 4).Style.NumberFormat.Format = "0.0%";
            payerRow++;
        }

        row = payerRow;

        // ---- rozkład miesięczny ----
        var monthStart = row + 3;
        ws.Cell(monthStart, 1).Value = "Rozkład miesięczny";
        ws.Cell(monthStart, 1).Style.Font.SetBold().Font.SetFontSize(12);

        ws.Cell(monthStart + 1, 1).Value = "Miesiąc";
        ws.Cell(monthStart + 1, 2).Value = "Dokumentów";
        ws.Cell(monthStart + 1, 3).Value = "Kwota";
        StyleHeader(ws.Range(monthStart + 1, 1, monthStart + 1, 3));

        var monthRow = monthStart + 2;
        foreach (var month in data.ByMonth)
        {
            ws.Cell(monthRow, 1).Value = $"{month.MonthName} {month.Year}";
            ws.Cell(monthRow, 2).Value = month.InvoicesCount;
            SetMoney(ws.Cell(monthRow, 3), month.Total);
            monthRow++;
        }

        ws.Columns().AdjustToContents();
    }

    // ================================================================ lista zakupów

    public byte[] BuildShoppingList(ShoppingReportData data)
    {
        using var workbook = new XLWorkbook();

        BuildShoppingItemsSheet(workbook, data);
        BuildShoppingSummarySheet(workbook, data);

        return Save(workbook);
    }

    private static void BuildShoppingItemsSheet(XLWorkbook workbook, ShoppingReportData data)
    {
        var ws = workbook.Worksheets.Add("Lista zakupów");

        ws.Cell(1, 1).Value = "Lista rzeczy do zakupu";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = data.PropertyName;
        ws.Cell(3, 1).Value = $"Wygenerowano: {Formatting.DateTimeLocal(data.GeneratedAtUtc)}";

        const int headerRow = 5;

        // Nagłówki 1:1 z arkuszem prowadzonym ręcznie + kolumny dołożone przez aplikację.
        string[] headers =
        [
            "L.p", "Pomieszczenie", "Kategoria", "Pozycja", "Opis", "Uwagi/obliczenia",
            "Ilość", "Jednostka", "~Koszt szt.", "~Koszt całk.", "Planowany budżet (z amortyzacją)",
            "Rzeczywisty koszt", "Finansuje", "Wykonawca/sklep", "Link", "Status", "Priorytet",
            "Data zakupu", "Faktura", "Kto kupuje"
        ];

        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(headerRow, i + 1).Value = headers[i];
        }

        StyleHeader(ws.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        var ordered = data.Items
            .OrderBy(i => i.RoomName == ShoppingConstants.WholePropertyRoomName ? 1 : 0)
            .ThenBy(i => i.RoomName, StringComparer.CurrentCulture)
            .ThenBy(i => i.OrdinalNo);

        foreach (var item in ordered)
        {
            ws.Cell(row, 1).Value = item.OrdinalNo;
            ws.Cell(row, 2).Value = item.RoomName;
            ws.Cell(row, 3).Value = item.CategoryName ?? string.Empty;
            ws.Cell(row, 4).Value = item.Name;
            ws.Cell(row, 5).Value = item.Description ?? string.Empty;
            ws.Cell(row, 6).Value = item.CalculationNotes ?? string.Empty;
            SetNumber(ws.Cell(row, 7), item.Quantity, "# ##0.###");
            ws.Cell(row, 8).Value = item.Unit ?? string.Empty;
            SetMoney(ws.Cell(row, 9), item.UnitCost);
            SetMoney(ws.Cell(row, 10), item.TotalCost);
            SetMoney(ws.Cell(row, 11), item.PlannedBudget);
            SetMoney(ws.Cell(row, 12), item.ActualCost);
            ws.Cell(row, 13).Value = item.PayerName ?? string.Empty;
            ws.Cell(row, 14).Value = item.Vendor ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(item.Link))
            {
                ws.Cell(row, 15).Value = item.Link;
                TrySetHyperlink(ws.Cell(row, 15), item.Link);
            }

            ws.Cell(row, 16).Value = PolishLabels.For(item.Status);
            ws.Cell(row, 17).Value = PolishLabels.For(item.Priority);
            SetDate(ws.Cell(row, 18), item.PurchaseDate);
            ws.Cell(row, 19).Value = item.InvoiceLabel ?? string.Empty;
            ws.Cell(row, 20).Value = item.AssignedTo ?? string.Empty;

            row++;
        }

        if (data.Items.Count > 0)
        {
            ws.Cell(row, 4).Value = "Razem";
            ws.Cell(row, 4).Style.Font.SetBold();

            foreach (var column in new[] { 10, 11, 12 })
            {
                var letter = ws.Column(column).ColumnLetter();
                ws.Cell(row, column).FormulaA1 = $"SUM({letter}{headerRow + 1}:{letter}{row - 1})";
                ws.Cell(row, column).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(row, column).Style.Font.SetBold();
            }

            ws.Range(row, 1, row, headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;

            ws.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        // Długie teksty potrafią rozjechać arkusz — ograniczamy szerokość.
        foreach (var column in new[] { 5, 6, 15 })
        {
            ws.Column(column).Width = Math.Min(ws.Column(column).Width, 40);
        }
    }

    private static void BuildShoppingSummarySheet(XLWorkbook workbook, ShoppingReportData data)
    {
        var ws = workbook.Worksheets.Add("Podsumowanie");
        var summary = data.Summary;

        ws.Cell(1, 1).Value = "Podsumowanie listy zakupów";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);

        var row = 3;
        void AddMetric(string label, string value)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = value;
            row++;
        }

        AddMetric("Pozycji łącznie", summary.ItemsCount.ToString());
        AddMetric("Kupione / zamontowane", summary.DoneCount.ToString());
        AddMetric("Postęp remontu", $"{Formatting.Number(summary.ProgressPercent)}%");
        AddMetric("Koszt szacowany", Formatting.Money(summary.TotalCost));
        AddMetric("Planowany budżet", Formatting.Money(summary.PlannedBudget));
        AddMetric("Koszt rzeczywisty", Formatting.Money(summary.ActualCost));
        AddMetric("Budżet − rzeczywistość", Formatting.Money(summary.BudgetDifference));

        row += 2;
        row = WriteGroupTable(ws, row, "Według pomieszczeń", summary.ByRoom);
        row += 2;
        row = WriteGroupTable(ws, row, "Według kategorii", summary.ByCategory);
        row += 2;
        row = WriteGroupTable(ws, row, "Według statusu", summary.ByStatus);
        row += 2;
        WriteGroupTable(ws, row, "Podział kosztów (kto finansuje)", summary.ByPayer);

        ws.Columns().AdjustToContents();
    }

    private static int WriteGroupTable(IXLWorksheet ws, int startRow, string title, List<ShoppingGroupTotalDto> groups)
    {
        ws.Cell(startRow, 1).Value = title;
        ws.Cell(startRow, 1).Style.Font.SetBold().Font.SetFontSize(11);

        var headerRow = startRow + 1;
        ws.Cell(headerRow, 1).Value = "Nazwa";
        ws.Cell(headerRow, 2).Value = "Pozycji";
        ws.Cell(headerRow, 3).Value = "Koszt";
        ws.Cell(headerRow, 4).Value = "Budżet";
        ws.Cell(headerRow, 5).Value = "Rzeczywisty";
        StyleHeader(ws.Range(headerRow, 1, headerRow, 5));

        var row = headerRow + 1;
        foreach (var group in groups)
        {
            ws.Cell(row, 1).Value = group.Key;
            ws.Cell(row, 2).Value = group.ItemsCount;
            SetMoney(ws.Cell(row, 3), group.TotalCost);
            SetMoney(ws.Cell(row, 4), group.PlannedBudget);
            SetMoney(ws.Cell(row, 5), group.ActualCost);
            row++;
        }

        return row;
    }

    // ================================================================ pomocnicze

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.SetBold();
        range.Style.Font.SetFontColor(XLColor.White);
        range.Style.Fill.SetBackgroundColor(XLColor.FromHtml(HeaderColor));
        range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void SetMoney(IXLCell cell, decimal? value) => SetNumber(cell, value, MoneyFormat);

    private static void SetNumber(IXLCell cell, decimal? value, string format)
    {
        if (!value.HasValue)
        {
            return;
        }

        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = format;
    }

    private static void SetDate(IXLCell cell, DateOnly? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        cell.Value = value.Value.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = DateFormat;
    }

    private static void TrySetHyperlink(IXLCell cell, string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            cell.SetHyperlink(new XLHyperlink(uri));
        }
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
