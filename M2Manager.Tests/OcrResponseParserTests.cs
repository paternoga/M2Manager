using M2Manager.Api.Services;

namespace M2Manager.Tests;

/// <summary>
/// Model bywa gadatliwy i nie zawsze trzyma się instrukcji „tylko JSON”.
/// Parser musi to znosić, bo alternatywą jest ręczne przepisywanie faktury.
/// </summary>
public class OcrResponseParserTests
{
    [Fact]
    public void Parse_CleanJson_ReadsAllFields()
    {
        const string json = """
            {
              "vendor": "Castorama Polska Sp. z o.o.",
              "amount": 1234.56,
              "currency": "PLN",
              "issueDate": "2026-07-14",
              "suggestedCategoryName": "Remont i materiały"
            }
            """;

        var result = OcrResponseParser.Parse(json);

        Assert.True(result.Success);
        Assert.Equal("Castorama Polska Sp. z o.o.", result.Vendor);
        Assert.Equal(1234.56m, result.Amount);
        Assert.Equal("PLN", result.Currency);
        Assert.Equal(new DateOnly(2026, 7, 14), result.IssueDate);
        Assert.Equal("Remont i materiały", result.SuggestedCategoryName);
    }

    [Fact]
    public void Parse_JsonWrappedInMarkdownFence_Works()
    {
        const string text = """
            ```json
            {"vendor": "Leroy Merlin", "amount": 89.99, "currency": "PLN", "issueDate": null, "suggestedCategoryName": null}
            ```
            """;

        var result = OcrResponseParser.Parse(text);

        Assert.True(result.Success);
        Assert.Equal("Leroy Merlin", result.Vendor);
        Assert.Equal(89.99m, result.Amount);
        Assert.Null(result.IssueDate);
        Assert.Null(result.SuggestedCategoryName);
    }

    [Fact]
    public void Parse_JsonSurroundedByCommentary_Works()
    {
        const string text = """
            Oto odczytane dane z paragonu:
            {"vendor": "Biedronka", "amount": 45.30, "currency": "PLN", "issueDate": "2026-03-02"}
            Daj znać, jeśli mam coś poprawić.
            """;

        var result = OcrResponseParser.Parse(text);

        Assert.True(result.Success);
        Assert.Equal("Biedronka", result.Vendor);
        Assert.Equal(45.30m, result.Amount);
    }

    [Fact]
    public void Parse_NestedBracesInStrings_DoNotBreakExtraction()
    {
        const string text = """
            {"vendor": "Sklep {Budowlany}", "amount": 10, "currency": "PLN"}
            """;

        var result = OcrResponseParser.Parse(text);

        Assert.True(result.Success);
        Assert.Equal("Sklep {Budowlany}", result.Vendor);
    }

    [Fact]
    public void Parse_TextWithoutJson_Fails_ButKeepsRawResponse()
    {
        const string text = "Niestety nie jestem w stanie odczytać tego zdjęcia.";

        var result = OcrResponseParser.Parse(text);

        Assert.False(result.Success);
        Assert.Equal(text, result.RawResponse);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_EmptyInput_Fails()
    {
        var result = OcrResponseParser.Parse("   ");

        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_StringNull_IsTreatedAsMissingValue()
    {
        const string json = """{"vendor": "null", "amount": null, "currency": "PLN"}""";

        var result = OcrResponseParser.Parse(json);

        Assert.True(result.Success);
        Assert.Null(result.Vendor);
        Assert.Null(result.Amount);
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1 234,56 zł", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("89", 89)]
    [InlineData("1.234", 1234)]
    [InlineData("12.5", 12.5)]
    public void ParseAmount_HandlesPolishAndInternationalFormats(string raw, decimal expected)
    {
        Assert.Equal(expected, OcrResponseParser.ParseAmount(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("brak")]
    [InlineData(null)]
    public void ParseAmount_UnreadableValues_ReturnNull(string? raw)
    {
        Assert.Null(OcrResponseParser.ParseAmount(raw));
    }

    [Theory]
    [InlineData("2026-07-14")]
    [InlineData("14.07.2026")]
    [InlineData("14-07-2026")]
    [InlineData("2026/07/14")]
    public void ParseDate_AcceptsCommonPolishFormats(string raw)
    {
        Assert.Equal(new DateOnly(2026, 7, 14), OcrResponseParser.ParseDate(raw));
    }

    [Fact]
    public void ParseDate_Garbage_ReturnsNull()
    {
        Assert.Null(OcrResponseParser.ParseDate("nieczytelna"));
    }

    [Theory]
    [InlineData("zł", "PLN")]
    [InlineData("pln", "PLN")]
    [InlineData("EUR", "EUR")]
    [InlineData("", "PLN")]
    [InlineData("nieznana waluta", "PLN")]
    public void Parse_NormalizesCurrency(string raw, string expected)
    {
        var json = $$"""{"vendor": "X", "amount": 1, "currency": "{{raw}}"}""";

        var result = OcrResponseParser.Parse(json);

        Assert.Equal(expected, result.Currency);
    }

    [Fact]
    public void Parse_AmountGivenAsString_IsStillRead()
    {
        const string json = """{"vendor": "X", "amount": "1 299,00 zł", "currency": "PLN"}""";

        var result = OcrResponseParser.Parse(json);

        Assert.Equal(1299.00m, result.Amount);
    }
}
