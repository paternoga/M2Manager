using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using M2Manager.Api.Configuration;
using M2Manager.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Services;

/// <summary>
/// Odczyt faktury przez Gemini API (generateContent) — zdjęcie jako base64 w `inlineData`
/// plus prośba o wyłącznie JSON. Dodatkowo wymuszamy `responseMimeType: application/json`,
/// dzięki czemu model nie owija odpowiedzi w blok markdown.
/// </summary>
public sealed class GeminiOcrService(
    HttpClient http,
    IOptions<GeminiOptions> options,
    ILogger<GeminiOcrService> logger) : IOcrService
{
    private readonly GeminiOptions _options = options.Value;

    /// <summary>
    /// Formaty przyjmowane przez Gemini. W przeciwieństwie do wielu innych API obsługuje HEIC/HEIF,
    /// czyli domyślny format zdjęć z iPhone'a — nie trzeba nic przestawiać w telefonie.
    /// </summary>
    private static readonly string[] SupportedMediaTypes =
    [
        "image/jpeg", "image/png", "image/webp", "image/heic", "image/heif", "application/pdf"
    ];

    public bool IsEnabled => _options.IsConfigured;

    public async Task<OcrExtractionResult> ExtractAsync(
        byte[] fileBytes,
        string contentType,
        IReadOnlyCollection<string> availableCategories,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return OcrExtractionResult.Failed("Brak klucza GEMINI_API_KEY.");
        }

        if (fileBytes.Length == 0)
        {
            return OcrExtractionResult.Failed("Pusty plik — nie ma czego odczytać.");
        }

        var mediaType = (contentType ?? string.Empty).ToLowerInvariant();

        if (!SupportedMediaTypes.Contains(mediaType))
        {
            return OcrExtractionResult.Failed(
                $"Format {contentType} nie jest obsługiwany przez odczyt AI. Zdjęcie zostało zapisane — uzupełnij dane ręcznie.");
        }

        var url = $"{_options.ApiVersion.Trim('/')}/models/{_options.Model}:generateContent";
        var payload = BuildRequest(fileBytes, mediaType, availableCategories);

        try
        {
            using var response = await http.PostAsJsonAsync(url, payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var apiMessage = ExtractApiError(body);
                logger.LogWarning("Gemini API zwróciło {Status}: {Message}", (int)response.StatusCode, apiMessage ?? body);

                return OcrExtractionResult.Failed(
                    $"Gemini API zwróciło błąd {(int)response.StatusCode}: {apiMessage ?? "brak szczegółów"}. Uzupełnij dane ręcznie.",
                    body);
            }

            if (ExtractBlockReason(body) is { } blockReason)
            {
                logger.LogWarning("Gemini odmówiło odpowiedzi: {Reason}", blockReason);
                return OcrExtractionResult.Failed(
                    $"Model odmówił analizy zdjęcia ({blockReason}). Uzupełnij dane ręcznie.", body);
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
            logger.LogWarning(ex, "Przekroczono czas oczekiwania na odpowiedź Gemini API.");
            return OcrExtractionResult.Failed("Przekroczono czas oczekiwania na odpowiedź AI. Uzupełnij dane ręcznie.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Błąd połączenia z Gemini API.");
            return OcrExtractionResult.Failed($"Błąd połączenia z AI: {ex.Message}");
        }
    }

    private Dictionary<string, object> BuildRequest(
        byte[] fileBytes,
        string mediaType,
        IReadOnlyCollection<string> categories)
    {
        var generationConfig = new Dictionary<string, object>
        {
            // Odczyt faktury ma być powtarzalny, nie kreatywny.
            ["temperature"] = 0,
            ["maxOutputTokens"] = _options.MaxOutputTokens,

            // Wymusza czysty JSON — model nie owija odpowiedzi w blok markdown.
            ["responseMimeType"] = "application/json"
        };

        return new Dictionary<string, object>
        {
            ["contents"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["inlineData"] = new Dictionary<string, object>
                            {
                                ["mimeType"] = mediaType,
                                ["data"] = Convert.ToBase64String(fileBytes)
                            }
                        },
                        new Dictionary<string, object> { ["text"] = BuildPrompt(categories) }
                    }
                }
            },
            ["generationConfig"] = generationConfig
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

    /// <summary>Skleja bloki tekstu z `candidates[0].content.parts[]`.</summary>
    internal static string? ExtractTextContent(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var candidate = candidates[0];

            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
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

    /// <summary>Powód odrzucenia zapytania przez filtry bezpieczeństwa albo ucięcia odpowiedzi.</summary>
    internal static string? ExtractBlockReason(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("promptFeedback", out var feedback) &&
                feedback.TryGetProperty("blockReason", out var blockReason) &&
                blockReason.ValueKind == JsonValueKind.String)
            {
                return blockReason.GetString();
            }

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("finishReason", out var finishReason) &&
                finishReason.ValueKind == JsonValueKind.String)
            {
                var reason = finishReason.GetString();

                // STOP to poprawne zakończenie; MAX_TOKENS obsłuży parser (JSON będzie niepełny).
                if (reason is not null and not "STOP" and not "MAX_TOKENS")
                {
                    return reason;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractApiError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            return doc.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
