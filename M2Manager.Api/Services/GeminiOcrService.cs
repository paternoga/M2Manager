using System.Net;
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

    /// <summary>Łącznie z pierwszą próbą. Trzy podejścia mieszczą się w limicie czasu uploadu.</summary>
    private const int MaxAttempts = 3;

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
        OcrCategories categories,
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
        var payload = BuildRequest(fileBytes, mediaType, categories);

        // Darmowy tier potrafi odpowiedzieć 503 „high demand” — to stan chwilowy, więc ponawiamy.
        // Bez tego użytkownik przepisuje fakturę ręcznie tylko dlatego, że akurat trafił na szczyt ruchu.
        for (var attempt = 1; ; attempt++)
        {
            var lastAttempt = attempt >= MaxAttempts;

            try
            {
                using var response = await http.PostAsJsonAsync(url, payload, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var apiMessage = ExtractApiError(body);

                    if (IsTransient(response.StatusCode) && !lastAttempt)
                    {
                        logger.LogInformation(
                            "Gemini API zwróciło {Status} (próba {Attempt}/{Max}) — ponawiam.",
                            (int)response.StatusCode, attempt, MaxAttempts);

                        await Task.Delay(RetryDelay(attempt), ct);
                        continue;
                    }

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
                if (!lastAttempt)
                {
                    logger.LogInformation(ex, "Błąd połączenia z Gemini (próba {Attempt}/{Max}) — ponawiam.", attempt, MaxAttempts);
                    await Task.Delay(RetryDelay(attempt), ct);
                    continue;
                }

                logger.LogWarning(ex, "Błąd połączenia z Gemini API.");
                return OcrExtractionResult.Failed($"Błąd połączenia z AI: {ex.Message}");
            }
        }
    }

    /// <summary>Błędy, które warto ponowić: przeciążenie modelu i limit zapytań.</summary>
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(2 * attempt);

    private Dictionary<string, object> BuildRequest(
        byte[] fileBytes,
        string mediaType,
        OcrCategories categories)
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

    internal static string BuildPrompt(OcrCategories categories)
    {
        static string Join(IReadOnlyCollection<string> values) =>
            values.Count > 0 ? string.Join(", ", values) : "brak zdefiniowanych kategorii";

        var sb = new StringBuilder();
        sb.AppendLine("Przeanalizuj załączone zdjęcie polskiej faktury lub paragonu.");
        sb.AppendLine("Zwróć WYŁĄCZNIE poprawny JSON (bez komentarzy, bez markdown) o strukturze:");
        sb.AppendLine("{");
        sb.AppendLine("  \"vendor\": \"nazwa sprzedawcy lub null\",");
        sb.AppendLine("  \"amount\": liczba (kwota brutto do zapłaty) lub null,");
        sb.AppendLine("  \"currency\": \"PLN\" lub inny kod waluty,");
        sb.AppendLine("  \"issueDate\": \"YYYY-MM-DD\" lub null,");
        sb.AppendLine("  \"suggestedCategoryName\": \"jedna z kategorii wydatku lub null\",");
        sb.AppendLine("  \"lineItems\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": \"nazwa pozycji dokładnie jak na dokumencie\",");
        sb.AppendLine("      \"quantity\": liczba lub null,");
        sb.AppendLine("      \"unit\": \"szt. / m² / opak. / l / kg lub null\",");
        sb.AppendLine("      \"unitPrice\": cena jednostkowa brutto lub null,");
        sb.AppendLine("      \"totalPrice\": wartość brutto tej pozycji lub null,");
        sb.AppendLine("      \"suggestedCategoryName\": \"jedna z kategorii zakupów lub null\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine($"Kategorie wydatku (dla całej faktury): {Join(categories.Expense)}.");
        sb.AppendLine($"Kategorie zakupów (dla pojedynczych pozycji): {Join(categories.Shopping)}.");
        sb.AppendLine();
        sb.AppendLine("W \"lineItems\" wypisz KAŻDĄ pozycję dokumentu osobno — klej do płytek i płytki");
        sb.AppendLine("to dwa różne wiersze, nawet jeśli są na jednej fakturze. Nie łącz ich i nie streszczaj.");
        sb.AppendLine("Pomiń wiersze, które nie są towarem (podsumowania, stawki VAT, rabaty).");
        sb.AppendLine("Kosztu dostawy NIE wypisuj jako osobnej pozycji:");
        sb.AppendLine("— gdy dokument ma tylko jeden towar, dolicz dostawę do jego wartości,");
        sb.AppendLine("  tak żeby \"totalPrice\" tej pozycji równał się kwocie z pola \"amount\";");
        sb.AppendLine("— gdy towarów jest kilka, podaj każdy z jego własną ceną i dostawy nie rozdzielaj.");
        sb.AppendLine("Gdy dokument pokazuje wyłącznie kwotę łączną, zwróć \"lineItems\": [].");
        sb.Append("Jeśli czegoś nie da się odczytać, wstaw null. Kwoty to wartości brutto (z VAT).");

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
