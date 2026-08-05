using M2Manager.Shared;
using M2Manager.Shared.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace M2Manager.Api.Services;

/// <summary>
/// Eksporty PDF (QuestPDF). Zestawienie kosztów idzie do banku i księgowej,
/// więc układ jest celowo oszczędny i czytelny: nagłówek, tabela, podsumowania.
/// </summary>
public sealed class PdfExportService
{
    private const string Accent = "#1f4e79";
    private const string SoftGrey = "#f2f4f7";

    // ================================================================ faktury

    public byte[] BuildInvoiceReport(InvoiceReportData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.4f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));

                page.Header().Element(c => ComposeReportHeader(c, data));
                page.Content().PaddingVertical(10).Element(c => ComposeReportContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeReportHeader(IContainer container, InvoiceReportData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Zestawienie kosztów").FontSize(18).SemiBold().FontColor(Accent);
                    left.Item().PaddingTop(2).Text(data.PropertyName).FontSize(12).SemiBold();
                    left.Item().Text($"Okres: {data.PeriodLabel}").FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                row.ConstantItem(160).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("Wygenerowano").FontSize(8).FontColor(Colors.Grey.Darken1);
                    right.Item().AlignRight().Text(Formatting.DateTimeLocal(data.GeneratedAtUtc)).FontSize(9);
                    right.Item().PaddingTop(6).AlignRight().Text($"Liczba dokumentów: {data.Rows.Count}").FontSize(9);
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1.2f).LineColor(Accent);
        });
    }

    private static void ComposeReportContent(IContainer container, InvoiceReportData data)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            // ---- suma główna ----
            column.Item().Background(SoftGrey).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Suma kosztów w okresie").FontSize(10).FontColor(Colors.Grey.Darken2);
                    if (data.MissingAmountCount > 0)
                    {
                        left.Item().PaddingTop(2)
                            .Text($"Uwaga: {data.MissingAmountCount} dokument(ów) bez wpisanej kwoty.")
                            .FontSize(8).FontColor(Colors.Red.Darken1);
                    }
                });

                row.ConstantItem(180).AlignRight().AlignMiddle()
                    .Text(Formatting.Money(data.Total, data.Currency))
                    .FontSize(16).Bold().FontColor(Accent);
            });

            // ---- tabela dokumentów ----
            column.Item().Column(section =>
            {
                section.Item().PaddingBottom(4).Text("Dokumenty").FontSize(11).SemiBold().FontColor(Accent);

                if (data.Rows.Count == 0)
                {
                    section.Item().Text("Brak dokumentów w wybranym okresie.").Italic().FontColor(Colors.Grey.Darken1);
                    return;
                }

                section.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(58);   // data
                        columns.RelativeColumn(2.2f); // sprzedawca
                        columns.RelativeColumn(1.9f); // kategoria
                        columns.RelativeColumn(1.4f); // pomieszczenie
                        columns.RelativeColumn(1.3f); // finansuje
                        columns.ConstantColumn(78);   // kwota
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Data");
                        header.Cell().Element(HeaderCell).Text("Sprzedawca");
                        header.Cell().Element(HeaderCell).Text("Kategoria");
                        header.Cell().Element(HeaderCell).Text("Pomieszczenie");
                        header.Cell().Element(HeaderCell).Text("Finansuje");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Kwota");
                    });

                    foreach (var row in data.Rows)
                    {
                        table.Cell().Element(BodyCell).Text(Formatting.Date(row.IssueDate));
                        table.Cell().Element(BodyCell).Text(Formatting.Text(row.Vendor));
                        table.Cell().Element(BodyCell).Text(Formatting.Text(row.Category));
                        table.Cell().Element(BodyCell).Text(Formatting.Text(row.Room));
                        table.Cell().Element(BodyCell).Text(Formatting.Text(row.Payer));
                        table.Cell().Element(BodyCell).AlignRight()
                            .Text(Formatting.Money(row.Amount, row.Currency));
                    }
                });
            });

            // ---- podsumowanie per kategoria ----
            if (data.ByCategory.Count > 0)
            {
                column.Item().Column(section =>
                {
                    section.Item().PaddingBottom(4).Text("Podsumowanie według kategorii")
                        .FontSize(11).SemiBold().FontColor(Accent);

                    section.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(90);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Kategoria");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Dokumentów");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Kwota");
                        });

                        foreach (var category in data.ByCategory)
                        {
                            table.Cell().Element(BodyCell).Text(category.CategoryName);
                            table.Cell().Element(BodyCell).AlignRight().Text(category.InvoicesCount.ToString());
                            table.Cell().Element(BodyCell).AlignRight()
                                .Text(Formatting.Money(category.Total, data.Currency));
                        }

                        table.Cell().Element(TotalCell).Text("Razem").SemiBold();
                        table.Cell().Element(TotalCell).AlignRight()
                            .Text(data.ByCategory.Sum(c => c.InvoicesCount).ToString()).SemiBold();
                        table.Cell().Element(TotalCell).AlignRight()
                            .Text(Formatting.Money(data.Total, data.Currency)).SemiBold();
                    });
                });
            }

            // ---- podział kosztów między osoby ----
            if (data.ByPayer.Count > 0)
            {
                column.Item().Column(section =>
                {
                    section.Item().PaddingBottom(4).Text("Podział kosztów")
                        .FontSize(11).SemiBold().FontColor(Accent);

                    section.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(90);
                            columns.ConstantColumn(60);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Kto finansuje");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Dokumentów");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Kwota");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Udział");
                        });

                        foreach (var payer in data.ByPayer)
                        {
                            table.Cell().Element(BodyCell).Text(payer.PayerName);
                            table.Cell().Element(BodyCell).AlignRight().Text(payer.InvoicesCount.ToString());
                            table.Cell().Element(BodyCell).AlignRight()
                                .Text(Formatting.Money(payer.Total, data.Currency));
                            table.Cell().Element(BodyCell).AlignRight()
                                .Text($"{Formatting.Number(payer.SharePercent)}%");
                        }
                    });
                });
            }

            // ---- podsumowanie miesięczne ----
            if (data.ByMonth.Count > 1)
            {
                column.Item().Column(section =>
                {
                    section.Item().PaddingBottom(4).Text("Rozkład miesięczny")
                        .FontSize(11).SemiBold().FontColor(Accent);

                    section.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(90);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Miesiąc");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Dokumentów");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Kwota");
                        });

                        foreach (var month in data.ByMonth)
                        {
                            table.Cell().Element(BodyCell).Text($"{month.MonthName} {month.Year}");
                            table.Cell().Element(BodyCell).AlignRight().Text(month.InvoicesCount.ToString());
                            table.Cell().Element(BodyCell).AlignRight()
                                .Text(Formatting.Money(month.Total, data.Currency));
                        }
                    });
                });
            }
        });
    }

    // ================================================================ lista zakupów

    public byte[] BuildShoppingList(ShoppingReportData data)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Black));

                page.Header().Element(c => ComposeShoppingHeader(c, data));
                page.Content().PaddingVertical(8).Element(c => ComposeShoppingContent(c, data));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeShoppingHeader(IContainer container, ShoppingReportData data)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Lista rzeczy do zakupu").FontSize(16).SemiBold().FontColor(Accent);
                    left.Item().Text(data.PropertyName).FontSize(11).SemiBold();
                });

                row.ConstantItem(220).AlignRight().Column(right =>
                {
                    right.Item().AlignRight()
                        .Text($"Pozycji: {data.Summary.ItemsCount} · gotowych: {data.Summary.DoneCount} " +
                              $"({Formatting.Number(data.Summary.ProgressPercent)}%)")
                        .FontSize(9);
                    right.Item().AlignRight()
                        .Text(Formatting.DateTimeLocal(data.GeneratedAtUtc))
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });

            column.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Accent);
        });
    }

    private static void ComposeShoppingContent(IContainer container, ShoppingReportData data)
    {
        container.Column(column =>
        {
            column.Spacing(12);

            column.Item().Background(SoftGrey).Padding(8).Row(row =>
            {
                AddSummaryBox(row, "Koszt (szacowany)", Formatting.Money(data.Summary.TotalCost));
                AddSummaryBox(row, "Planowany budżet", Formatting.Money(data.Summary.PlannedBudget));
                AddSummaryBox(row, "Koszt rzeczywisty", Formatting.Money(data.Summary.ActualCost));
                AddSummaryBox(row, "Budżet − rzeczywistość", Formatting.Money(data.Summary.BudgetDifference));
            });

            if (data.Items.Count == 0)
            {
                column.Item().Text("Lista jest pusta.").Italic().FontColor(Colors.Grey.Darken1);
                return;
            }

            // Grupujemy po pomieszczeniach — tak samo jak domyślny widok w aplikacji.
            var groups = data.Items
                .GroupBy(i => i.RoomName)
                .OrderBy(g => g.Key == ShoppingConstants.WholePropertyRoomName ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.CurrentCulture);

            foreach (var group in groups)
            {
                column.Item().Column(section =>
                {
                    var groupTotal = group.Sum(i => i.TotalCost ?? 0m);

                    section.Item().PaddingBottom(3).Row(row =>
                    {
                        row.RelativeItem().Text(group.Key).FontSize(11).SemiBold().FontColor(Accent);
                        row.ConstantItem(160).AlignRight()
                            .Text($"{group.Count()} poz. · {Formatting.Money(groupTotal)}")
                            .FontSize(9).SemiBold();
                    });

                    section.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);   // L.p
                            columns.RelativeColumn(1.4f); // kategoria
                            columns.RelativeColumn(2.6f); // pozycja
                            columns.RelativeColumn(2.4f); // uwagi/obliczenia
                            columns.ConstantColumn(48);   // ilość
                            columns.ConstantColumn(58);   // koszt szt.
                            columns.ConstantColumn(62);   // koszt całk.
                            columns.ConstantColumn(62);   // budżet
                            columns.ConstantColumn(62);   // rzeczywisty
                            columns.ConstantColumn(60);   // finansuje
                            columns.ConstantColumn(58);   // status
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("L.p");
                            header.Cell().Element(HeaderCell).Text("Kategoria");
                            header.Cell().Element(HeaderCell).Text("Pozycja");
                            header.Cell().Element(HeaderCell).Text("Uwagi/obliczenia");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Ilość");
                            header.Cell().Element(HeaderCell).AlignRight().Text("~Koszt szt.");
                            header.Cell().Element(HeaderCell).AlignRight().Text("~Koszt całk.");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Budżet");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Rzeczyw.");
                            header.Cell().Element(HeaderCell).Text("Finansuje");
                            header.Cell().Element(HeaderCell).Text("Status");
                        });

                        foreach (var item in group.OrderBy(i => i.OrdinalNo))
                        {
                            table.Cell().Element(BodyCell).Text(item.OrdinalNo.ToString());
                            table.Cell().Element(BodyCell).Text(Formatting.Text(item.CategoryName));

                            table.Cell().Element(BodyCell).Column(cell =>
                            {
                                cell.Item().Text(item.Name).SemiBold();
                                if (!string.IsNullOrWhiteSpace(item.Description))
                                {
                                    cell.Item().Text(item.Description).FontSize(7).FontColor(Colors.Grey.Darken2);
                                }
                            });

                            table.Cell().Element(BodyCell).Text(Formatting.Text(item.CalculationNotes)).FontSize(7);

                            table.Cell().Element(BodyCell).AlignRight()
                                .Text($"{Formatting.Quantity(item.Quantity)} {item.Unit}".Trim());
                            table.Cell().Element(BodyCell).AlignRight().Text(Formatting.Number(item.UnitCost));
                            table.Cell().Element(BodyCell).AlignRight().Text(Formatting.Number(item.TotalCost));
                            table.Cell().Element(BodyCell).AlignRight().Text(Formatting.Number(item.PlannedBudget));
                            table.Cell().Element(BodyCell).AlignRight().Text(Formatting.Number(item.ActualCost));
                            table.Cell().Element(BodyCell).Text(Formatting.Text(item.PayerName)).FontSize(7);
                            table.Cell().Element(BodyCell).Text(PolishLabels.For(item.Status)).FontSize(7);
                        }
                    });
                });
            }
        });
    }

    private static void AddSummaryBox(RowDescriptor row, string label, string value)
    {
        row.RelativeItem().Column(column =>
        {
            column.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
            column.Item().Text(value).FontSize(12).SemiBold().FontColor(Accent);
        });
    }

    // ================================================================ style

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("M2Manager").FontSize(8).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span("Strona ");
                text.CurrentPageNumber();
                text.Span(" z ");
                text.TotalPages();
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container
            .Background(Accent)
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .DefaultTextStyle(t => t.FontColor(Colors.White).SemiBold().FontSize(8));

    private static IContainer BodyCell(IContainer container) =>
        container
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3)
            .PaddingHorizontal(4);

    private static IContainer TotalCell(IContainer container) =>
        container
            .Background(SoftGrey)
            .BorderTop(1)
            .BorderColor(Accent)
            .PaddingVertical(4)
            .PaddingHorizontal(4);
}
