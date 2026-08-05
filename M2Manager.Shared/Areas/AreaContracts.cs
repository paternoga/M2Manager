namespace M2Manager.Shared.Areas;

/// <summary>
/// Minimum danych o pomieszczeniu potrzebne do wyliczenia powierzchni.
/// Implementują to zarówno encje po stronie API, jak i DTO po stronie Blazora,
/// dzięki czemu edytor rzutu liczy dokładnie tym samym kodem co serwer.
/// </summary>
public interface IRoomAreaSource
{
    decimal? FloorAreaM2 { get; }
    decimal? LengthM { get; }
    decimal? WidthM { get; }
    decimal? HeightM { get; }
    decimal? ManualWallAreaM2 { get; }
    decimal? ExcludedWallAreaM2 { get; }
    decimal? ManualCeilingAreaM2 { get; }
    bool IncludeInTotals { get; }
    string? GeometryJson { get; }
}

/// <summary>Minimum danych o otworze (okno/drzwi) potrzebne do wyliczeń.</summary>
public interface IOpeningAreaSource
{
    decimal WidthCm { get; }
    decimal HeightCm { get; }
    int Count { get; }
    bool SubtractFromWalls { get; }
}

/// <summary>Komplet wyliczeń dla jednego pomieszczenia. Wszystko zaokrąglone do 2 miejsc.</summary>
public sealed record RoomAreaResult
{
    /// <summary>Obwód w metrach; null, gdy brak wymiarów i geometrii.</summary>
    public decimal? PerimeterM { get; init; }

    /// <summary>Wysokość użyta w wyliczeniach (własna pomieszczenia albo domyślna mieszkania).</summary>
    public decimal? HeightM { get; init; }

    public decimal? FloorAreaM2 { get; init; }

    /// <summary>Ściany brutto = obwód × wysokość.</summary>
    public decimal? GrossWallAreaM2 { get; init; }

    /// <summary>Suma powierzchni otworów oznaczonych do odjęcia.</summary>
    public decimal OpeningsAreaM2 { get; init; }

    /// <summary>Ściany netto — do malowania/gruntowania.</summary>
    public decimal? NetWallAreaM2 { get; init; }

    public decimal? CeilingAreaM2 { get; init; }

    /// <summary>Ściany netto + sufit; null tylko wtedy, gdy obie składowe są nieznane.</summary>
    public decimal? WallsAndCeilingM2 { get; init; }
}

/// <summary>Podsumowanie całego mieszkania — liczone wyłącznie z pomieszczeń z IncludeInTotals.</summary>
public sealed record PropertyAreaSummary
{
    public int RoomsCounted { get; init; }
    public decimal TotalFloorAreaM2 { get; init; }
    public decimal TotalNetWallAreaM2 { get; init; }
    public decimal TotalCeilingAreaM2 { get; init; }
    public decimal TotalWallsAndCeilingM2 { get; init; }
}
