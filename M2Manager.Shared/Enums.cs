namespace M2Manager.Shared;

/// <summary>Przeznaczenie mieszkania — własne albo pod wynajem.</summary>
public enum PropertyPurpose
{
    OwnOccupied = 0,
    Rental = 1
}

/// <summary>Status odczytu faktury przez AI. `Confirmed` ustawia człowiek po weryfikacji.</summary>
public enum OcrStatus
{
    Pending = 0,
    Extracted = 1,
    Failed = 2,
    Confirmed = 3
}

/// <summary>Rodzaj otworu w ścianie.</summary>
public enum OpeningType
{
    Window = 0,
    Door = 1,
    Doorway = 2,
    Other = 3
}

/// <summary>Strona świata ściany — używana wyłącznie do umiejscowienia otworu na rzucie.</summary>
public enum WallSide
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}

/// <summary>Status pozycji na liście zakupów/remontu.</summary>
public enum ShoppingStatus
{
    ToBuy = 0,
    Ordered = 1,
    Bought = 2,
    Installed = 3,
    Cancelled = 4
}

/// <summary>Priorytet pozycji — pozwala oddzielić „musi być” od „fajnie by było”.</summary>
public enum ShoppingPriority
{
    MustHave = 0,
    NiceToHave = 1,
    Optional = 2
}

/// <summary>
/// Polskie etykiety enumów. Trzymamy je w Shared, żeby UI, PDF i Excel
/// nazywały te same wartości dokładnie tak samo.
/// </summary>
public static class PolishLabels
{
    public static string For(PropertyPurpose value) => value switch
    {
        PropertyPurpose.OwnOccupied => "Własne",
        PropertyPurpose.Rental => "Na wynajem",
        _ => value.ToString()
    };

    public static string For(OcrStatus value) => value switch
    {
        OcrStatus.Pending => "Oczekuje",
        OcrStatus.Extracted => "Odczytana przez AI",
        OcrStatus.Failed => "Odczyt nieudany",
        OcrStatus.Confirmed => "Zatwierdzona",
        _ => value.ToString()
    };

    public static string For(OpeningType value) => value switch
    {
        OpeningType.Window => "Okno",
        OpeningType.Door => "Drzwi",
        OpeningType.Doorway => "Otwór drzwiowy",
        OpeningType.Other => "Inny",
        _ => value.ToString()
    };

    public static string For(WallSide value) => value switch
    {
        WallSide.North => "Północ (góra)",
        WallSide.East => "Wschód (prawo)",
        WallSide.South => "Południe (dół)",
        WallSide.West => "Zachód (lewo)",
        _ => value.ToString()
    };

    public static string For(ShoppingStatus value) => value switch
    {
        ShoppingStatus.ToBuy => "Do kupienia",
        ShoppingStatus.Ordered => "Zamówione",
        ShoppingStatus.Bought => "Kupione",
        ShoppingStatus.Installed => "Zamontowane",
        ShoppingStatus.Cancelled => "Zrezygnowano",
        _ => value.ToString()
    };

    public static string For(ShoppingPriority value) => value switch
    {
        ShoppingPriority.MustHave => "Musi być",
        ShoppingPriority.NiceToHave => "Fajnie by było",
        ShoppingPriority.Optional => "Opcjonalne",
        _ => value.ToString()
    };
}
