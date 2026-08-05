using M2Manager.Api.Services;

namespace M2Manager.Tests;

/// <summary>
/// Kształt odpowiedzi Gemini (`generateContent`) — testy pilnują, że wyciągamy tekst
/// z właściwego miejsca i poprawnie rozpoznajemy odmowę modelu.
/// </summary>
public class GeminiOcrServiceTests
{
    [Fact]
    public void ExtractTextContent_ReadsTextFromFirstCandidate()
    {
        const string response = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [ { "text": "{\"vendor\": \"Castorama\", \"amount\": 249.99}" } ],
                    "role": "model"
                  },
                  "finishReason": "STOP"
                }
              ],
              "usageMetadata": { "promptTokenCount": 1204, "candidatesTokenCount": 38 },
              "modelVersion": "gemini-2.5-flash"
            }
            """;

        var text = GeminiOcrService.ExtractTextContent(response);

        Assert.Equal("{\"vendor\": \"Castorama\", \"amount\": 249.99}", text);
    }

    [Fact]
    public void ExtractTextContent_JoinsMultipleParts()
    {
        const string response = """
            {
              "candidates": [
                { "content": { "parts": [ { "text": "{\"vendor\":" }, { "text": " \"Leroy Merlin\"}" } ] } }
              ]
            }
            """;

        Assert.Equal("{\"vendor\": \"Leroy Merlin\"}", GeminiOcrService.ExtractTextContent(response));
    }

    [Fact]
    public void ExtractTextContent_EndToEndWithParser()
    {
        const string response = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [ { "text": "{\"vendor\": \"Castorama\", \"amount\": 249.99, \"currency\": \"PLN\", \"issueDate\": \"2026-07-14\", \"suggestedCategoryName\": \"Remont i materiały\"}" } ]
                  },
                  "finishReason": "STOP"
                }
              ]
            }
            """;

        var result = OcrResponseParser.Parse(GeminiOcrService.ExtractTextContent(response));

        Assert.True(result.Success);
        Assert.Equal("Castorama", result.Vendor);
        Assert.Equal(249.99m, result.Amount);
        Assert.Equal("PLN", result.Currency);
        Assert.Equal(new DateOnly(2026, 7, 14), result.IssueDate);
        Assert.Equal("Remont i materiały", result.SuggestedCategoryName);
    }

    [Fact]
    public void ExtractTextContent_MissingCandidates_ReturnsNull()
    {
        Assert.Null(GeminiOcrService.ExtractTextContent("""{"usageMetadata": {}}"""));
        Assert.Null(GeminiOcrService.ExtractTextContent("""{"candidates": []}"""));
        Assert.Null(GeminiOcrService.ExtractTextContent("to nie jest json"));
    }

    [Fact]
    public void ExtractTextContent_PartsWithoutText_ReturnsNull()
    {
        const string response = """
            {"candidates": [ { "content": { "parts": [ { "functionCall": {} } ] } } ]}
            """;

        Assert.Null(GeminiOcrService.ExtractTextContent(response));
    }

    [Fact]
    public void ExtractBlockReason_DetectsPromptFeedbackBlock()
    {
        const string response = """
            {"promptFeedback": {"blockReason": "SAFETY", "safetyRatings": []}}
            """;

        Assert.Equal("SAFETY", GeminiOcrService.ExtractBlockReason(response));
    }

    [Fact]
    public void ExtractBlockReason_DetectsCandidateFinishReason()
    {
        const string response = """
            {"candidates": [ { "finishReason": "RECITATION", "index": 0 } ]}
            """;

        Assert.Equal("RECITATION", GeminiOcrService.ExtractBlockReason(response));
    }

    [Theory]
    [InlineData("STOP")]
    [InlineData("MAX_TOKENS")]
    public void ExtractBlockReason_NormalFinishReasons_AreNotTreatedAsBlock(string finishReason)
    {
        var response = $$"""
            {"candidates": [ { "finishReason": "{{finishReason}}", "content": { "parts": [ { "text": "{}" } ] } } ]}
            """;

        Assert.Null(GeminiOcrService.ExtractBlockReason(response));
    }

    [Fact]
    public void ExtractBlockReason_MalformedJson_ReturnsNull()
    {
        Assert.Null(GeminiOcrService.ExtractBlockReason("<html>502 Bad Gateway</html>"));
    }

    [Fact]
    public void BuildPrompt_ListsAvailableCategories()
    {
        var prompt = GeminiOcrService.BuildPrompt(["Media (prąd, gaz, woda)", "Remont i materiały"]);

        Assert.Contains("Dostępne kategorie: Media (prąd, gaz, woda), Remont i materiały.", prompt);
        Assert.Contains("WYŁĄCZNIE poprawny JSON", prompt);
        Assert.Contains("\"issueDate\": \"YYYY-MM-DD\"", prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutCategories_StillProducesValidInstruction()
    {
        var prompt = GeminiOcrService.BuildPrompt([]);

        Assert.Contains("brak zdefiniowanych kategorii", prompt);
    }
}
