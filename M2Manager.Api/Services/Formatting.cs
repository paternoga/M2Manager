using System.Globalization;

namespace M2Manager.Api.Services;

/// <summary>Wspólne formatowanie liczb i dat w eksportach — wszystko po polsku.</summary>
public static class Formatting
{
    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Money(decimal? value, string currency = "PLN") =>
        value.HasValue
            ? $"{value.Value.ToString("N2", Pl)} {currency}"
            : "—";

    public static string Number(decimal? value) =>
        value.HasValue ? value.Value.ToString("N2", Pl) : "—";

    public static string Quantity(decimal? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        var rounded = Math.Round(value.Value, 3, MidpointRounding.AwayFromZero);
        return rounded == Math.Truncate(rounded)
            ? rounded.ToString("0", Pl)
            : rounded.ToString("0.###", Pl);
    }

    public static string Date(DateOnly? value) =>
        value?.ToString("dd.MM.yyyy", Pl) ?? "—";

    public static string DateTimeLocal(DateTime utc) =>
        utc.ToString("dd.MM.yyyy HH:mm", Pl) + " UTC";

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    /// <summary>Nazwa miesiąca po polsku, wielką literą („Styczeń”).</summary>
    public static string MonthName(int month)
    {
        if (month is < 1 or > 12)
        {
            return "—";
        }

        var name = Pl.DateTimeFormat.GetMonthName(month);
        return char.ToUpper(name[0], Pl) + name[1..];
    }

    /// <summary>Etykieta okresu: „2026” albo „Lipiec 2026”.</summary>
    public static string PeriodLabel(int year, int? month) =>
        month.HasValue ? $"{MonthName(month.Value)} {year}" : year.ToString(Pl);
}
