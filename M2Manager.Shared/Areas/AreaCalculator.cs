namespace M2Manager.Shared.Areas;

/// <summary>
/// Serce modułu powierzchni. Czysta funkcja — bez bazy, bez I/O, w pełni testowalna.
///
/// Reguły (zgodnie z tym, jak liczy się to ręcznie w arkuszu):
///   obwód          = 2 × (długość + szerokość), a gdy brak wymiarów — z geometrii rzutu
///   ściany brutto  = obwód × wysokość
///   otwory         = Σ (szer_cm/100 × wys_cm/100 × sztuk) dla otworów do odjęcia
///   ściany netto   = ręczne nadpisanie ?? (brutto − otwory − wyłączone)
///   sufit          = ręczne nadpisanie ?? metraż podłogi
/// </summary>
public static class AreaCalculator
{
    /// <summary>Wysokość używana, gdy ani pomieszczenie, ani mieszkanie jej nie podaje.</summary>
    public const decimal FallbackRoomHeightM = 2.60m;

    public static RoomAreaResult Calculate(
        IRoomAreaSource room,
        IEnumerable<IOpeningAreaSource>? openings,
        decimal? defaultRoomHeightM = null)
    {
        ArgumentNullException.ThrowIfNull(room);

        var geometry = RoomGeometry.Parse(room.GeometryJson);

        var height = FirstPositive(room.HeightM, defaultRoomHeightM) ?? FallbackRoomHeightM;
        var floorArea = ResolveFloorArea(room, geometry);
        var perimeter = ResolvePerimeter(room, geometry);

        decimal? grossWall = perimeter.HasValue ? perimeter.Value * height : null;

        var openingsArea = SumOpenings(openings);

        decimal? netWall;
        if (room.ManualWallAreaM2.HasValue)
        {
            // Ręczne nadpisanie wygrywa zawsze — pomieszczenia nieprostokątne liczymy poza aplikacją.
            netWall = room.ManualWallAreaM2.Value;
        }
        else if (grossWall.HasValue)
        {
            netWall = grossWall.Value - openingsArea - (room.ExcludedWallAreaM2 ?? 0m);
            if (netWall < 0m)
            {
                netWall = 0m;
            }
        }
        else
        {
            netWall = null;
        }

        var ceiling = room.ManualCeilingAreaM2 ?? floorArea;

        decimal? wallsAndCeiling = netWall.HasValue || ceiling.HasValue
            ? (netWall ?? 0m) + (ceiling ?? 0m)
            : null;

        return new RoomAreaResult
        {
            PerimeterM = Round2(perimeter),
            HeightM = Round2(height),
            FloorAreaM2 = Round2(floorArea),
            GrossWallAreaM2 = Round2(grossWall),
            OpeningsAreaM2 = Round2(openingsArea) ?? 0m,
            NetWallAreaM2 = Round2(netWall),
            CeilingAreaM2 = Round2(ceiling),
            WallsAndCeilingM2 = Round2(wallsAndCeiling)
        };
    }

    /// <summary>
    /// Podsumowanie mieszkania. Bierze pod uwagę wyłącznie pomieszczenia z <c>IncludeInTotals = true</c>
    /// (dzięki temu ogródek nie zawyża metrażu do malowania).
    /// </summary>
    public static PropertyAreaSummary Summarize(IEnumerable<(IRoomAreaSource Room, RoomAreaResult Result)> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);

        var counted = 0;
        decimal floor = 0, walls = 0, ceilings = 0;

        foreach (var (room, result) in rooms)
        {
            if (!room.IncludeInTotals)
            {
                continue;
            }

            counted++;
            floor += result.FloorAreaM2 ?? 0m;
            walls += result.NetWallAreaM2 ?? 0m;
            ceilings += result.CeilingAreaM2 ?? 0m;
        }

        return new PropertyAreaSummary
        {
            RoomsCounted = counted,
            TotalFloorAreaM2 = Round2(floor) ?? 0m,
            TotalNetWallAreaM2 = Round2(walls) ?? 0m,
            TotalCeilingAreaM2 = Round2(ceilings) ?? 0m,
            TotalWallsAndCeilingM2 = Round2(walls + ceilings) ?? 0m
        };
    }

    /// <summary>Metraż: ręczny → z wymiarów → z geometrii rzutu.</summary>
    private static decimal? ResolveFloorArea(IRoomAreaSource room, RoomGeometry? geometry)
    {
        if (room.FloorAreaM2 is > 0m)
        {
            return room.FloorAreaM2;
        }

        if (room.LengthM is > 0m && room.WidthM is > 0m)
        {
            return room.LengthM.Value * room.WidthM.Value;
        }

        return geometry?.AreaM2();
    }

    /// <summary>Obwód: z wymiarów → z geometrii rzutu. Sam metraż nie wystarcza (nie zna kształtu).</summary>
    private static decimal? ResolvePerimeter(IRoomAreaSource room, RoomGeometry? geometry)
    {
        if (room.LengthM is > 0m && room.WidthM is > 0m)
        {
            return 2m * (room.LengthM.Value + room.WidthM.Value);
        }

        return geometry?.PerimeterM();
    }

    private static decimal SumOpenings(IEnumerable<IOpeningAreaSource>? openings)
    {
        if (openings is null)
        {
            return 0m;
        }

        decimal sum = 0m;
        foreach (var opening in openings)
        {
            if (!opening.SubtractFromWalls)
            {
                continue;
            }

            var count = opening.Count > 0 ? opening.Count : 1;
            sum += opening.WidthCm / 100m * (opening.HeightCm / 100m) * count;
        }

        return sum;
    }

    private static decimal? FirstPositive(params decimal?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is > 0m)
            {
                return candidate;
            }
        }

        return null;
    }

    private static decimal? Round2(decimal? value) =>
        value.HasValue ? Math.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;
}
