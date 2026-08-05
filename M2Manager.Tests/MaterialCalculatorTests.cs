using M2Manager.Shared.Areas;

namespace M2Manager.Tests;

public class MaterialCalculatorTests
{
    /// <summary>Przykład z opisu: „105 m² ścian × 2 warstwy ÷ 10 m²/l = 21 l gruntu”.</summary>
    [Fact]
    public void Calculate_PrimerExample_Returns21Litres()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 105m,
            Coats = 2,
            CoveragePerUnit = 10m,
            Unit = "l"
        });

        Assert.Equal(210m, result.TotalAreaM2);
        Assert.Equal(21m, result.RawQuantity);
        Assert.Equal(21m, result.Quantity);
        Assert.Equal("105 m² × 2 warstwy ÷ 10 m²/l = 21 l", result.Explanation);
    }

    [Fact]
    public void Calculate_RoundsUpToWholeUnits()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 100m,
            Coats = 2,
            CoveragePerUnit = 12m,
            Unit = "l"
        });

        Assert.Equal(16.67m, result.RawQuantity); // 200 / 12
        Assert.Equal(17m, result.Quantity);
    }

    [Fact]
    public void Calculate_WithReserve_AddsPercentBeforeRounding()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 20m,
            Coats = 1,
            CoveragePerUnit = 1m,
            Unit = "m²",
            ReservePercent = 10m
        });

        Assert.Equal(22m, result.Quantity);
        Assert.Contains("10% zapasu", result.Explanation);
    }

    [Fact]
    public void Calculate_WithoutRoundUp_KeepsTwoDecimals()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 100m,
            Coats = 1,
            CoveragePerUnit = 3m,
            Unit = "opak.",
            RoundUp = false
        });

        Assert.Equal(33.33m, result.Quantity);
    }

    [Fact]
    public void Calculate_SingleCoat_OmitsCoatsFromExplanation()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 50m,
            Coats = 1,
            CoveragePerUnit = 10m,
            Unit = "l"
        });

        Assert.Equal("50 m² ÷ 10 m²/l = 5 l", result.Explanation);
    }

    [Fact]
    public void Calculate_ZeroCoverage_ReturnsHelpfulMessageInsteadOfDividingByZero()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 50m,
            CoveragePerUnit = 0m
        });

        Assert.Equal(0m, result.Quantity);
        Assert.Contains("wydajność", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_ZeroCoats_TreatedAsOne()
    {
        var result = MaterialCalculator.Calculate(new MaterialCalculationInput
        {
            AreaM2 = 30m,
            Coats = 0,
            CoveragePerUnit = 10m
        });

        Assert.Equal(3m, result.Quantity);
    }
}
