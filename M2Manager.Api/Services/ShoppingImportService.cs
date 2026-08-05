using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using M2Manager.Api.Data;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Services;

/// <summary>
/// Import listy zakupów z arkusza .xlsx. Nagłówki dopasowujemy „na miękko”
/// (bez wielkości liter, bez polskich znaków, bez znaków specjalnych), bo w praktyce
/// w arkuszu pojawiają się warianty typu „~Koszt szt.” albo „Wykonawca/sklep”.
/// </summary>
public sealed class ShoppingImportService(AppDbContext db, ILogger<ShoppingImportService> logger)
{
    /// <summary>Mapa: znormalizowany nagłówek → pole docelowe.</summary>
    private static readonly Dictionary<string, ShoppingColumn> HeaderMap = BuildHeaderMap();

    public async Task<ShoppingImportResultDto> ImportAsync(
        Stream xlsxStream,
        int propertyId,
        CancellationToken ct = default)
    {
        var result = new ShoppingImportResultDto();

        using var workbook = new XLWorkbook(xlsxStream);
        var worksheet = PickWorksheet(workbook);

        if (worksheet is null)
        {
            result.Warnings.Add("Arkusz jest pusty — nie znaleziono żadnych danych.");
            return result;
        }

        var (headerRow, columns) = FindHeaderRow(worksheet);
        if (headerRow == 0 || !columns.ContainsValue(ShoppingColumn.Name))
        {
            result.Warnings.Add(
                "Nie znaleziono wiersza nagłówków z kolumną „Pozycja”. Sprawdź, czy arkusz ma nagłówki w pierwszych wierszach.");
            return result;
        }

        // Słowniki ładujemy raz i uzupełniamy w pamięci, żeby nie odpytywać bazy per wiersz.
        var rooms = await db.Rooms
            .Where(r => r.PropertyId == propertyId)
            .ToDictionaryAsync(r => Normalize(r.Name), r => r, ct);

        var categories = await db.ShoppingCategories
            .ToDictionaryAsync(c => Normalize(c.Name), c => c, ct);

        var nextOrdinal = (await db.ShoppingItems
            .Where(i => i.PropertyId == propertyId)
            .MaxAsync(i => (int?)i.OrdinalNo, ct) ?? 0) + 1;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            ct.ThrowIfCancellationRequested();

            var row = worksheet.Row(rowNumber);
            var name = GetString(row, columns, ShoppingColumn.Name);

            if (string.IsNullOrWhiteSpace(name))
            {
                result.SkippedCount++;
                continue;
            }

            var item = new ShoppingItem
            {
                PropertyId = propertyId,
                Name = name.Trim(),
                Description = GetString(row, columns, ShoppingColumn.Description),
                CalculationNotes = GetString(row, columns, ShoppingColumn.CalculationNotes),
                Quantity = GetDecimal(row, columns, ShoppingColumn.Quantity),
                Unit = GetString(row, columns, ShoppingColumn.Unit),
                UnitCost = GetDecimal(row, columns, ShoppingColumn.UnitCost),
                TotalCost = GetDecimal(row, columns, ShoppingColumn.TotalCost),
                PlannedBudget = GetDecimal(row, columns, ShoppingColumn.PlannedBudget),
                ActualCost = GetDecimal(row, columns, ShoppingColumn.ActualCost),
                Vendor = GetString(row, columns, ShoppingColumn.Vendor),
                Link = GetString(row, columns, ShoppingColumn.Link),
                AssignedTo = GetString(row, columns, ShoppingColumn.AssignedTo),
                PurchaseDate = GetDate(row, columns, ShoppingColumn.PurchaseDate),
                Status = ParseStatus(GetString(row, columns, ShoppingColumn.Status)),
                Priority = ParsePriority(GetString(row, columns, ShoppingColumn.Priority), name),
                OrdinalNo = GetInt(row, columns, ShoppingColumn.OrdinalNo) ?? nextOrdinal
            };

            // Koszt całkowity: jeśli arkusz go nie ma, wyliczamy z ilości i ceny jednostkowej.
            if (!item.TotalCost.HasValue && item is { Quantity: not null, UnitCost: not null })
            {
                item.TotalCost = Math.Round(item.Quantity.Value * item.UnitCost.Value, 2, MidpointRounding.AwayFromZero);
            }

            item.RoomId = ResolveRoom(
                GetString(row, columns, ShoppingColumn.Room),
                propertyId,
                rooms,
                result);

            item.ShoppingCategoryId = ResolveCategory(
                GetString(row, columns, ShoppingColumn.Category),
                categories,
                result);

            db.ShoppingItems.Add(item);
            result.ImportedCount++;
            nextOrdinal = Math.Max(nextOrdinal, item.OrdinalNo) + 1;
        }

        if (result.ImportedCount > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Zaimportowano {Imported} pozycji do mieszkania {PropertyId} (pominięto {Skipped}).",
            result.ImportedCount, propertyId, result.SkippedCount);

        return result;
    }

    // ---------------------------------------------------------------- słowniki

    private int? ResolveRoom(
        string? rawName,
        int propertyId,
        Dictionary<string, Room> rooms,
        ShoppingImportResultDto result)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var normalized = Normalize(rawName);

        // „Całe mieszkanie” to brak przypisania do pokoju.
        if (normalized == Normalize(ShoppingConstants.WholePropertyRoomName) || normalized.Length == 0)
        {
            return null;
        }

        if (rooms.TryGetValue(normalized, out var existing))
        {
            return existing.Id;
        }

        var created = new Room
        {
            PropertyId = propertyId,
            Name = rawName.Trim(),
            SortOrder = (rooms.Count + 1) * 10
        };

        db.Rooms.Add(created);
        db.SaveChanges(); // potrzebujemy Id od razu, żeby podpiąć kolejne wiersze

        rooms[normalized] = created;
        result.CreatedRooms.Add(created.Name);

        return created.Id;
    }

    private int? ResolveCategory(
        string? rawName,
        Dictionary<string, ShoppingCategory> categories,
        ShoppingImportResultDto result)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var normalized = Normalize(rawName);
        if (normalized.Length == 0)
        {
            return null;
        }

        if (categories.TryGetValue(normalized, out var existing))
        {
            return existing.Id;
        }

        var created = new ShoppingCategory
        {
            Name = rawName.Trim(),
            SortOrder = (categories.Count + 1) * 10
        };

        db.ShoppingCategories.Add(created);
        db.SaveChanges();

        categories[normalized] = created;
        result.CreatedCategories.Add(created.Name);

        return created.Id;
    }

    // ---------------------------------------------------------------- odczyt arkusza

    private static IXLWorksheet? PickWorksheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(w => w.LastRowUsed() is not null)
        ?? workbook.Worksheets.FirstOrDefault();

    /// <summary>Szuka wiersza nagłówków w pierwszych 15 wierszach arkusza.</summary>
    internal static (int HeaderRow, Dictionary<int, ShoppingColumn> Columns) FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 1, 15);
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;

        var bestRow = 0;
        Dictionary<int, ShoppingColumn> bestColumns = [];

        for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
        {
            Dictionary<int, ShoppingColumn> found = [];

            for (var columnNumber = 1; columnNumber <= lastColumn; columnNumber++)
            {
                var text = worksheet.Cell(rowNumber, columnNumber).GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (HeaderMap.TryGetValue(Normalize(text), out var column) && !found.ContainsValue(column))
                {
                    found[columnNumber] = column;
                }
            }

            if (found.Count > bestColumns.Count)
            {
                bestRow = rowNumber;
                bestColumns = found;
            }
        }

        return bestColumns.Count >= 2 ? (bestRow, bestColumns) : (0, []);
    }

    private static string? GetString(IXLRow row, Dictionary<int, ShoppingColumn> columns, ShoppingColumn column)
    {
        var cell = FindCell(row, columns, column);
        var text = cell?.GetString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static decimal? GetDecimal(IXLRow row, Dictionary<int, ShoppingColumn> columns, ShoppingColumn column)
    {
        var cell = FindCell(row, columns, column);
        if (cell is null || cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number))
        {
            return Math.Round((decimal)number, 2, MidpointRounding.AwayFromZero);
        }

        return OcrResponseParser.ParseAmount(cell.GetString());
    }

    private static int? GetInt(IXLRow row, Dictionary<int, ShoppingColumn> columns, ShoppingColumn column)
    {
        var value = GetDecimal(row, columns, column);
        return value.HasValue ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;
    }

    private static DateOnly? GetDate(IXLRow row, Dictionary<int, ShoppingColumn> columns, ShoppingColumn column)
    {
        var cell = FindCell(row, columns, column);
        if (cell is null || cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return OcrResponseParser.ParseDate(cell.GetString());
    }

    private static IXLCell? FindCell(IXLRow row, Dictionary<int, ShoppingColumn> columns, ShoppingColumn column)
    {
        foreach (var (index, mapped) in columns)
        {
            if (mapped == column)
            {
                return row.Cell(index);
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- parsowanie enumów

    internal static ShoppingStatus ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ShoppingStatus.ToBuy;
        }

        return Normalize(raw) switch
        {
            "kupione" or "kupione" or "bought" or "zakupione" => ShoppingStatus.Bought,
            "zamowione" or "ordered" => ShoppingStatus.Ordered,
            "zamontowane" or "installed" or "zrobione" => ShoppingStatus.Installed,
            "zrezygnowano" or "cancelled" or "anulowane" or "rezygnacja" => ShoppingStatus.Cancelled,
            _ => ShoppingStatus.ToBuy
        };
    }

    /// <summary>Bez kolumny priorytetu stosujemy konwencję z arkusza: znak zapytania = „fajnie by było”.</summary>
    internal static ShoppingPriority ParsePriority(string? raw, string itemName)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return Normalize(raw) switch
            {
                "musibyc" or "musthave" or "wysoki" or "must" => ShoppingPriority.MustHave,
                "fajniebybylo" or "nicetohave" or "sredni" => ShoppingPriority.NiceToHave,
                "opcjonalne" or "optional" or "niski" => ShoppingPriority.Optional,
                _ => ShoppingPriority.MustHave
            };
        }

        return itemName.Contains('?') ? ShoppingPriority.NiceToHave : ShoppingPriority.MustHave;
    }

    // ---------------------------------------------------------------- normalizacja

    /// <summary>„~Koszt szt.” → „kosztszt”. Pozwala dopasować nagłówki mimo literówek w interpunkcji.</summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        // „ł” nie rozkłada się przez FormD — trzeba je zamienić ręcznie.
        return sb.ToString().Replace('ł', 'l');
    }

    private static Dictionary<string, ShoppingColumn> BuildHeaderMap()
    {
        var map = new Dictionary<string, ShoppingColumn>(StringComparer.Ordinal);

        void Add(ShoppingColumn column, params string[] headers)
        {
            foreach (var header in headers)
            {
                map[Normalize(header)] = column;
            }
        }

        Add(ShoppingColumn.OrdinalNo, "L.p", "L.p.", "Lp", "Lp.", "Nr");
        Add(ShoppingColumn.Room, "Pomieszczenie", "Pokój", "Miejsce");
        Add(ShoppingColumn.Category, "Kategoria", "Grupa");
        Add(ShoppingColumn.Name, "Pozycja", "Nazwa", "Przedmiot", "Co");
        Add(ShoppingColumn.Description, "Opis");
        Add(ShoppingColumn.CalculationNotes, "Uwagi/obliczenia", "Uwagi-obliczenia", "Uwagi", "Obliczenia", "Notatki");
        Add(ShoppingColumn.Quantity, "Ilość", "Ilosc", "Liczba");
        Add(ShoppingColumn.Unit, "Jednostka", "Jm", "J.m.");
        Add(ShoppingColumn.UnitCost, "~Koszt szt.", "Koszt szt.", "Koszt szt", "Cena szt.", "Cena jednostkowa");
        Add(ShoppingColumn.TotalCost, "~Koszt całk.", "Koszt całk.", "Koszt całk", "Koszt całkowity", "Razem");
        Add(ShoppingColumn.PlannedBudget, "Planowany budżet (z amortyzacją)", "Planowany budżet", "Budżet");
        Add(ShoppingColumn.ActualCost, "Rzeczywisty koszt", "Koszt rzeczywisty");
        Add(ShoppingColumn.Vendor, "Wykonawca/sklep", "Wykonawca-sklep", "Wykonawca", "Sklep", "Dostawca");
        Add(ShoppingColumn.Link, "Link", "Adres", "URL");
        Add(ShoppingColumn.Status, "Status");
        Add(ShoppingColumn.Priority, "Priorytet");
        Add(ShoppingColumn.PurchaseDate, "Data zakupu", "Data");
        Add(ShoppingColumn.AssignedTo, "Kto kupuje", "Odpowiedzialny", "Kto");

        return map;
    }
}

internal enum ShoppingColumn
{
    OrdinalNo,
    Room,
    Category,
    Name,
    Description,
    CalculationNotes,
    Quantity,
    Unit,
    UnitCost,
    TotalCost,
    PlannedBudget,
    ActualCost,
    Vendor,
    Link,
    Status,
    Priority,
    PurchaseDate,
    AssignedTo
}
