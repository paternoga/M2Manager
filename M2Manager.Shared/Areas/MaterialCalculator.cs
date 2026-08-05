using System.Globalization;

namespace M2Manager.Shared.Areas;

/// <summary>Dane wejściowe kalkulatora materiałów.</summary>
public sealed class MaterialCalculationInput
{
    /// <summary>Powierzchnia do pokrycia w m² (np. ściany netto albo ściany + sufit).</summary>
    public decimal AreaM2 { get; set; }

    /// <summary>Liczba warstw.</summary>
    public int Coats { get; set; } = 1;

    /// <summary>Wydajność: ile m² pokrywa jedna jednostka (np. 10 m²/l).</summary>
    public decimal CoveragePerUnit { get; set; }

    /// <summary>Jednostka wyniku: l, opak., szt., m².</summary>
    public string Unit { get; set; } = "l";

    /// <summary>Zapas w procentach (np. 10 dla docinania płytek).</summary>
    public decimal ReservePercent { get; set; }

    /// <summary>Czy zaokrąglać w górę do pełnej jednostki (farba w puszkach — tak, m² — nie).</summary>
    public bool RoundUp { get; set; } = true;
}

/// <summary>Wynik kalkulacji wraz z gotowym opisem do wklejenia w „Uwagi/obliczenia”.</summary>
public sealed record MaterialCalculationResult
{
    public decimal TotalAreaM2 { get; init; }
    public decimal RawQuantity { get; init; }
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "l";

    /// <summary>Np. „105 m² × 2 warstwy ÷ 10 m²/l = 21 l”.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Kalkulator materiałów: powierzchnia × warstwy ÷ wydajność (+ zapas).
/// Wynik trafia jednym kliknięciem na listę zakupów razem z opisem obliczenia.
/// </summary>
public static class MaterialCalculator
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static MaterialCalculationResult Calculate(MaterialCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var coats = input.Coats > 0 ? input.Coats : 1;
        var unit = string.IsNullOrWhiteSpace(input.Unit) ? "szt." : input.Unit.Trim();
        var totalArea = input.AreaM2 * coats;

        if (input.CoveragePerUnit <= 0m)
        {
            return new MaterialCalculationResult
            {
                TotalAreaM2 = Round2(totalArea),
                RawQuantity = 0m,
                Quantity = 0m,
                Unit = unit,
                Explanation = "Podaj wydajność większą od zera, żeby policzyć ilość."
            };
        }

        var raw = totalArea / input.CoveragePerUnit;

        var withReserve = input.ReservePercent > 0m
            ? raw * (1m + (input.ReservePercent / 100m))
            : raw;

        var quantity = input.RoundUp
            ? Math.Ceiling(withReserve)
            : Math.Round(withReserve, 2, MidpointRounding.AwayFromZero);

        var explanation = BuildExplanation(input, coats, unit, quantity);

        return new MaterialCalculationResult
        {
            TotalAreaM2 = Round2(totalArea),
            RawQuantity = Round2(raw),
            Quantity = quantity,
            Unit = unit,
            Explanation = explanation
        };
    }

    private static string BuildExplanation(MaterialCalculationInput input, int coats, string unit, decimal quantity)
    {
        var text = coats > 1
            ? $"{Fmt(input.AreaM2)} m² × {coats} warstwy ÷ {Fmt(input.CoveragePerUnit)} m²/{unit}"
            : $"{Fmt(input.AreaM2)} m² ÷ {Fmt(input.CoveragePerUnit)} m²/{unit}";

        if (input.ReservePercent > 0m)
        {
            text += $" + {Fmt(input.ReservePercent)}% zapasu";
        }

        return $"{text} = {Fmt(quantity)} {unit}";
    }

    /// <summary>Formatowanie po polsku, bez zbędnych zer na końcu (2,5 zamiast 2,50).</summary>
    private static string Fmt(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == Math.Truncate(rounded)
            ? rounded.ToString("0", Pl)
            : rounded.ToString("0.##", Pl);
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
