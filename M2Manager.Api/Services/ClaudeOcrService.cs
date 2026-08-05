using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using M2Manager.Api.Configuration;
using M2Manager.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Services;

/// <summary>
/// Odczyt faktury przez Claude Messages API (model z obsługą wizji).
/// Zdjęcie idzie jako base64, a model ma zwrócić wyłącznie JSON.
/// </summary>
public sealed class ClaudeOcrService(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<ClaudeOcrService> logger) : IOcrService
{
    private readonly AnthropicOptions _options = options.Value;

    /// <summary>Formaty obrazu przyjmowane przez Messages API.</summary>
    private static readonly string[] SupportedImageTypes =
        ["image/jpeg", "image/png", "image/gif", "image/webp"];

    public bool IsEnabled => _options.IsConfigured;

    public async Task<OcrExtractionResult> ExtractAsync(
        byte[] fileBytes,
        string contentType,
        IReadOnlyCollection<string> availableCategories,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return OcrExtractionResult.Failed("Brak klucza ANTHROPIC_API_KEY.");
        }

        if (fileBytes.Length == 0)
        {
            return OcrExtractionResult.Failed("Pusty plik — nie ma czego odczytać.");
        }

        var normalizedType = (contentType ?? string.Empty).ToLowerInvariant();
        var isPdf = normalizedType == "application/pdf";

        if (!isPdf && !SupportedImageTypes.Contains(normalizedType))
        {
            // HEIC z iPhone'a trafia tu, jeśli telefon nie przekonwertował zdjęcia.
            return OcrExtractionResult.Failed(
                $"Format {contentType} nie jest obsługiwany przez odczyt AI. Zdjęcie zostało zapisane — uzupełnij dane ręcznie.");
        }

        var payload = BuildRequest(fileBytes, normalizedType, isPdf, availableCategories);

        try
        {
            using var response = await http.PostAsJsonAsync("/v1/messages", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Claude API zwróciło {Status}: {Body}", (int)response.StatusCode, body);
                return OcrExtractionResult.Failed(
                    $"Claude API zwróciło błąd {(int)response.StatusCode}. Uzupełnij dane ręcznie.", body);
            }

            var text = ExtractTextContent(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                return OcrExtractionResult.Failed("Odpowiedź modelu nie zawiera treści tekstowej.", body);
            }

            return OcrResponseParser.Parse(text);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Przekroczono czas oczekiwania na odpowiedź Claude API.");
            return OcrExtractionResult.Failed("Przekroczono czas oczekiwania na odpowiedź AI. Uzupełnij dane ręcznie.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Błąd połączenia z Claude API.");
            return OcrExtractionResult.Failed($"Błąd połączenia z AI: {ex.Message}");
        }
    }

    private object BuildRequest(byte[] fileBytes, string mediaType, bool isPdf, IReadOnlyCollection<string> categories)
    {
        var data = Convert.ToBase64String(fileBytes);

        object sourceBlock = isPdf
            ? new
            {
                type = "document",
                source = new { type = "base64", media_type = "application/pdf", data }
            }
            : new
            {
                type = "image",
                source = new { type = "base64", media_type = mediaType, data }
            };

        return new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new[]
                    {
                        sourceBlock,
                        new { type = "text", text = BuildPrompt(categories) }
                    }
                }
            }
        };
    }

    internal static string BuildPrompt(IReadOnlyCollection<string> categories)
    {
        var list = categories.Count > 0
            ? string.Join(", ", categories)
            : "brak zdefiniowanych kategorii";

        var sb = new StringBuilder();
        sb.AppendLine("Przeanalizuj załączone zdjęcie polskiej faktury lub paragonu.");
        sb.AppendLine("Zwróć WYŁĄCZNIE poprawny JSON (bez komentarzy, bez markdown) o strukturze:");
        sb.AppendLine("{");
        sb.AppendLine("  \"vendor\": \"nazwa sprzedawcy lub null\",");
        sb.AppendLine("  \"amount\": liczba (kwota brutto do zapłaty) lub null,");
        sb.AppendLine("  \"currency\": \"PLN\" lub inny kod waluty,");
        sb.AppendLine("  \"issueDate\": \"YYYY-MM-DD\" lub null,");
        sb.AppendLine("  \"suggestedCategoryName\": \"jedna z podanych kategorii lub null\"");
        sb.AppendLine("}");
        sb.AppendLine($"Dostępne kategorie: {list}.");
        sb.Append("Jeśli czegoś nie da się odczytać, wstaw null. Kwota to wartość brutto (z VAT).");

        return sb.ToString();
    }

    /// <summary>Skleja wszystkie bloki typu „text” z odpowiedzi Messages API.</summary>
    internal static string? ExtractTextContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
