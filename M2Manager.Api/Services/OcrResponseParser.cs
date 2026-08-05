using System.Globalization;
using System.Text.Json;
using M2Manager.Shared.Dtos;

namespace M2Manager.Api.Services;

/// <summary>
/// Wyciąga ustrukturyzowane dane z odpowiedzi modelu. Model bywa gadatliwy —
/// tolerujemy bloki ```json, tekst dookoła JSON-a oraz kwoty zapisane po polsku („1 234,56 zł”).
/// </summary>
public static class OcrResponseParser
{
    public static OcrExtractionResult Parse(string? modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return OcrExtractionResult.Failed("Model nie zwrócił żadnej treści.");
        }

        var json = ExtractJsonObject(modelText);
        if (json is null)
        {
            return OcrExtractionResult.Failed("Odpowiedź modelu nie zawiera poprawnego JSON-a.", modelText);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return OcrExtractionResult.Failed("Odpowiedź modelu nie jest obiektem JSON.", modelText);
            }

            return new OcrExtractionResult
            {
                Success = true,
                Vendor = ReadString(root, "vendor"),
                Amount = ReadDecimal(root, "amount"),
                Currency = NormalizeCurrency(ReadString(root, "currency")),
                IssueDate = ReadDate(root, "issueDate"),
                SuggestedCategoryName = ReadString(root, "suggestedCategoryName"),
                LineItems = ReadLineItems(root),
                RawResponse = modelText
            };
        }
        catch (JsonException ex)
        {
            return OcrExtractionResult.Failed($"Nie udało się sparsować JSON-a: {ex.Message}", modelText);
        }
    }

    /// <summary>Znajduje pierwszy kompletny obiekt JSON, pomijając bloki markdown i komentarz modelu.</summary>
    internal static string? ExtractJsonObject(string text)
    {
        var working = text.Trim();

        // Zdejmij ogrodzenie ```json ... ```
        if (working.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = working.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                working = working[(firstNewLine + 1)..];
            }

            var closingFence = working.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                working = working[..closingFence];
            }

            working = working.Trim();
        }

        var start = working.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        // Zliczamy nawiasy, ignorując te wewnątrz stringów.
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < working.Length; i++)
        {
            var c = working[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return working[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Pozycje faktury. Wiersz bez nazwy jest bezużyteczny na liście zakupów, więc go pomijamy;
    /// brak tablicy „lineItems” to normalna sytuacja (paragon z samą sumą), nie błąd.
    /// </summary>
    internal static List<OcrLineItemDto> ReadLineItems(JsonElement root)
    {
        var items = new List<OcrLineItemDto>();

        if (!root.TryGetProperty("lineItems", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var quantity = ReadDecimal(element, "quantity");
            var unitPrice = ReadDecimal(element, "unitPrice");
            var totalPrice = ReadDecimal(element, "totalPrice");

            // Modele często podają dwie z trzech wartości — trzecią doliczamy sami.
            if (totalPrice is null && quantity is not null && unitPrice is not null)
            {
                totalPrice = Math.Round(quantity.Value * unitPrice.Value, 2, MidpointRounding.AwayFromZero);
            }
            else if (unitPrice is null && totalPrice is not null && quantity is > 0m)
            {
                unitPrice = Math.Round(totalPrice.Value / quantity.Value, 2, MidpointRounding.AwayFromZero);
            }

            items.Add(new OcrLineItemDto
            {
                Name = name,
                Quantity = quantity,
                Unit = ReadString(element, "unit"),
                UnitPrice = unitPrice,
                TotalPrice = totalPrice,
                SuggestedCategoryName = ReadString(element, "suggestedCategoryName")
            });
        }

        return items;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        var value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        // Model czasem wpisuje słowo „null” zamiast wartości null.
        return value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    internal static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return element.ValueKind == JsonValueKind.String
            ? ParseAmount(element.GetString())
            : null;
    }

    /// <summary>Radzi sobie z „1 234,56 zł”, „1.234,56”, „1234.56” i „1 234”.</summary>
    internal static decimal? ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw
            .Where(c => char.IsDigit(c) || c is '.' or ',' or '-')
            .ToArray());

        if (cleaned.Length == 0)
        {
            return null;
        }

        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');

        if (lastDot >= 0 && lastComma >= 0)
        {
            // Ten separator, który występuje później, jest dziesiętny.
            if (lastComma > lastDot)
            {
                cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.');
            }
            else
            {
                cleaned = cleaned.Replace(",", string.Empty);
            }
        }
        else if (lastComma >= 0)
        {
            cleaned = cleaned.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            // „1.234” z jedną kropką i trzema cyframi po niej to zapewne separator tysięcy.
            var decimals = cleaned.Length - lastDot - 1;
            var dotCount = cleaned.Count(c => c == '.');
            if (dotCount > 1 || decimals == 3)
            {
                cleaned = cleaned.Replace(".", string.Empty);
            }
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    private static DateOnly? ReadDate(JsonElement root, string name)
    {
        var raw = ReadString(root, name);
        return ParseDate(raw);
    }

    internal static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy.MM.dd"
        ];

        if (DateOnly.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeCurrency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "PLN";
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed is "ZŁ" or "ZL" or "PLN")
        {
            return "PLN";
        }

        return trimmed.Length == 3 && trimmed.All(char.IsLetter) ? trimmed : "PLN";
    }
}
