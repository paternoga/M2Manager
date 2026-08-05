using System.Globalization;
using M2Manager.Shared;

namespace M2Manager.Client.Services;

/// <summary>Formatowanie do wyświetlania — po polsku i spójnie na wszystkich stronach.</summary>
public static class Fmt
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Money(decimal? value, string currency = "PLN") =>
        value.HasValue ? $"{value.Value.ToString("N2", Pl)} {currency}" : "—";

    public static string MoneyShort(decimal? value) =>
        value.HasValue ? value.Value.ToString("N2", Pl) : "—";

    public static string Area(decimal? value) =>
        value.HasValue ? $"{value.Value.ToString("N2", Pl)} m²" : "—";

    public static string Length(decimal? value) =>
        value.HasValue ? $"{value.Value.ToString("0.##", Pl)} m" : "—";

    public static string Number(decimal? value, int decimals = 2) =>
        value.HasValue ? value.Value.ToString($"N{decimals}", Pl) : "—";

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

    public static string Percent(decimal value) =>
        $"{value.ToString("0.#", Pl)}%";

    public static string Date(DateOnly? value) =>
        value?.ToString("dd.MM.yyyy", Pl) ?? "—";

    public static string Date(DateTime value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy", Pl);

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static string MonthName(int month)
    {
        if (month is < 1 or > 12)
        {
            return "—";
        }

        var name = Pl.DateTimeFormat.GetMonthName(month);
        return char.ToUpper(name[0], Pl) + name[1..];
    }

    /// <summary>Klasa CSS odznaki dla statusu pozycji zakupowej.</summary>
    public static string StatusBadge(ShoppingStatus status) => status switch
    {
        ShoppingStatus.Bought => "badge badge-info",
        ShoppingStatus.Installed => "badge badge-ok",
        ShoppingStatus.Ordered => "badge badge-warn",
        ShoppingStatus.Cancelled => "badge badge-danger",
        _ => "badge"
    };

    public static string OcrBadge(OcrStatus status) => status switch
    {
        OcrStatus.Confirmed => "badge badge-ok",
        OcrStatus.Extracted => "badge badge-info",
        OcrStatus.Failed => "badge badge-danger",
        _ => "badge badge-warn"
    };

    public static string PriorityBadge(ShoppingPriority priority) => priority switch
    {
        ShoppingPriority.MustHave => "badge badge-warn",
        ShoppingPriority.Optional => "badge",
        _ => "badge badge-info"
    };
}
