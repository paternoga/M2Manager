using M2Manager.Shared;
using M2Manager.Shared.Areas;
using M2Manager.Shared.Dtos;

namespace M2Manager.Tests;

/// <summary>
/// Serce modułu powierzchni — te liczby muszą się zgadzać co do metra
/// z tym, co dotąd było liczone ręcznie w arkuszu.
/// </summary>
public class AreaCalculatorTests
{
    /// <summary>
    /// Przykład referencyjny z arkusza: sypialnia 3,72 × 2,59 m, wysokość 2,60 m,
    /// drzwi 90×200 cm, okno 60×90 cm.
    /// 2*(3,72+2,59)*2,60 − 0,90*2,00 − 0,60*0,90 = 32,81 − 1,80 − 0,54 = 30,47 m²
    /// </summary>
    [Fact]
    public void Calculate_ReferenceBedroom_MatchesManualSpreadsheet()
    {
        var room = new RoomDto
        {
            Name = "Sypialnia",
            LengthM = 3.72m,
            WidthM = 2.59m
        };

        var openings = new List<RoomOpeningDto>
        {
            new() { Type = OpeningType.Door, WidthCm = 90, HeightCm = 200 },
            new() { Type = OpeningType.Window, WidthCm = 60, HeightCm = 90 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(12.62m, result.PerimeterM);
        Assert.Equal(2.60m, result.HeightM);
        Assert.Equal(32.81m, result.GrossWallAreaM2);
        Assert.Equal(2.34m, result.OpeningsAreaM2);
        Assert.Equal(30.47m, result.NetWallAreaM2);
    }

    [Fact]
    public void Calculate_FloorArea_ComputedFromDimensions()
    {
        var room = new RoomDto { LengthM = 3.72m, WidthM = 2.59m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        // 3,72 × 2,59 = 9,6348 → 9,63
        Assert.Equal(9.63m, result.FloorAreaM2);
        Assert.Equal(9.63m, result.CeilingAreaM2);
    }

    [Fact]
    public void Calculate_ManualFloorArea_WinsOverDimensions()
    {
        var room = new RoomDto { LengthM = 3.00m, WidthM = 3.00m, FloorAreaM2 = 8.50m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Equal(8.50m, result.FloorAreaM2);

        // Obwód nadal liczymy z wymiarów — metraż nie mówi nic o kształcie.
        Assert.Equal(12.00m, result.PerimeterM);
    }

    [Fact]
    public void Calculate_RoomHeight_OverridesPropertyDefault()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m, HeightM = 3.10m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Equal(3.10m, result.HeightM);
        Assert.Equal(43.40m, result.GrossWallAreaM2); // 14 × 3,10
    }

    [Fact]
    public void Calculate_WithoutAnyHeight_UsesFallback()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m };

        var result = AreaCalculator.Calculate(room, null, null);

        Assert.Equal(AreaCalculator.FallbackRoomHeightM, result.HeightM);
        Assert.Equal(36.40m, result.GrossWallAreaM2); // 14 × 2,60
    }

    [Fact]
    public void Calculate_ManualWallArea_OverridesEverything()
    {
        var room = new RoomDto
        {
            LengthM = 4m,
            WidthM = 3m,
            ManualWallAreaM2 = 25.00m,
            ExcludedWallAreaM2 = 5.00m
        };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 200, HeightCm = 200 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(25.00m, result.NetWallAreaM2);

        // Brutto i otwory nadal raportujemy — użytkownik ma widzieć, co nadpisał.
        Assert.Equal(36.40m, result.GrossWallAreaM2);
        Assert.Equal(4.00m, result.OpeningsAreaM2);
    }

    [Fact]
    public void Calculate_ExcludedWallArea_IsSubtracted()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m, ExcludedWallAreaM2 = 6.40m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Equal(30.00m, result.NetWallAreaM2); // 36,40 − 6,40
    }

    [Fact]
    public void Calculate_OpeningsNotMarkedForSubtraction_AreIgnored()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 100, HeightCm = 100, SubtractFromWalls = false },
            new() { WidthCm = 100, HeightCm = 100, SubtractFromWalls = true }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(1.00m, result.OpeningsAreaM2);
        Assert.Equal(35.40m, result.NetWallAreaM2);
    }

    [Fact]
    public void Calculate_OpeningCount_MultipliesArea()
    {
        var room = new RoomDto { LengthM = 5m, WidthM = 4m };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 120, HeightCm = 150, Count = 3 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(5.40m, result.OpeningsAreaM2); // 1,2 × 1,5 × 3
    }

    [Fact]
    public void Calculate_ZeroCount_TreatedAsOne()
    {
        var room = new RoomDto { LengthM = 5m, WidthM = 4m };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 100, HeightCm = 200, Count = 0 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(2.00m, result.OpeningsAreaM2);
    }

    [Fact]
    public void Calculate_OpeningsLargerThanWalls_ClampNetAreaToZero()
    {
        var room = new RoomDto { LengthM = 1m, WidthM = 1m };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 500, HeightCm = 500 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        Assert.Equal(0m, result.NetWallAreaM2);
    }

    [Fact]
    public void Calculate_ManualCeiling_OverridesFloorArea()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m, ManualCeilingAreaM2 = 10.00m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Equal(12.00m, result.FloorAreaM2);
        Assert.Equal(10.00m, result.CeilingAreaM2);
    }

    [Fact]
    public void Calculate_WallsAndCeiling_IsSumOfBoth()
    {
        var room = new RoomDto { LengthM = 4m, WidthM = 3m };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Equal(48.40m, result.WallsAndCeilingM2); // 36,40 + 12,00
    }

    [Fact]
    public void Calculate_WithoutDimensionsOrGeometry_ReturnsNulls()
    {
        var room = new RoomDto { Name = "Nieopisane" };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Null(result.PerimeterM);
        Assert.Null(result.GrossWallAreaM2);
        Assert.Null(result.NetWallAreaM2);
        Assert.Null(result.FloorAreaM2);
        Assert.Null(result.CeilingAreaM2);
        Assert.Null(result.WallsAndCeilingM2);
    }

    [Fact]
    public void Calculate_FallsBackToRectangleGeometry_WhenDimensionsMissing()
    {
        var geometry = new RoomGeometry { X = 0, Y = 0, WidthCm = 372, HeightCm = 259 };

        var room = new RoomDto { GeometryJson = geometry.ToJson() };

        var openings = new List<RoomOpeningDto>
        {
            new() { WidthCm = 90, HeightCm = 200 },
            new() { WidthCm = 60, HeightCm = 90 }
        };

        var result = AreaCalculator.Calculate(room, openings, 2.60m);

        // Ten sam pokój narysowany zamiast wpisany musi dać ten sam wynik.
        Assert.Equal(12.62m, result.PerimeterM);
        Assert.Equal(9.63m, result.FloorAreaM2);
        Assert.Equal(30.47m, result.NetWallAreaM2);
    }

    [Fact]
    public void Calculate_SupportsLShapedPolygonGeometry()
    {
        // Litera „L”: 400×300 cm z wyciętym prostokątem 200×150 cm w prawym dolnym rogu.
        var geometry = new RoomGeometry
        {
            Points =
            [
                new GeometryPoint(0, 0),
                new GeometryPoint(400, 0),
                new GeometryPoint(400, 150),
                new GeometryPoint(200, 150),
                new GeometryPoint(200, 300),
                new GeometryPoint(0, 300)
            ]
        };

        var room = new RoomDto { GeometryJson = geometry.ToJson() };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        // Obwód: 400 + 150 + 200 + 150 + 200 + 300 = 1400 cm = 14 m
        Assert.Equal(14.00m, result.PerimeterM);

        // Powierzchnia: 4,0 × 1,5 + 2,0 × 1,5 = 9,00 m²
        Assert.Equal(9.00m, result.FloorAreaM2);
        Assert.Equal(36.40m, result.GrossWallAreaM2);
    }

    [Fact]
    public void Calculate_InvalidGeometryJson_IsIgnored()
    {
        var room = new RoomDto { GeometryJson = "{to nie jest json" };

        var result = AreaCalculator.Calculate(room, null, 2.60m);

        Assert.Null(result.PerimeterM);
        Assert.Null(result.FloorAreaM2);
    }

    // ---------------------------------------------------------------- podsumowanie mieszkania

    [Fact]
    public void Summarize_SkipsRoomsExcludedFromTotals()
    {
        var livingRoom = new RoomDto { Name = "Salon", LengthM = 5m, WidthM = 4m };
        var garden = new RoomDto { Name = "Ogródek", LengthM = 6m, WidthM = 4m, IncludeInTotals = false };

        var rooms = new List<(IRoomAreaSource, RoomAreaResult)>
        {
            (livingRoom, AreaCalculator.Calculate(livingRoom, null, 2.60m)),
            (garden, AreaCalculator.Calculate(garden, null, 2.60m))
        };

        var summary = AreaCalculator.Summarize(rooms);

        Assert.Equal(1, summary.RoomsCounted);
        Assert.Equal(20.00m, summary.TotalFloorAreaM2);
        Assert.Equal(46.80m, summary.TotalNetWallAreaM2); // 2*(5+4)*2,60
        Assert.Equal(20.00m, summary.TotalCeilingAreaM2);
        Assert.Equal(66.80m, summary.TotalWallsAndCeilingM2);
    }

    [Fact]
    public void Summarize_AddsUpMultipleRooms()
    {
        var rooms = new List<RoomDto>
        {
            new() { Name = "Salon", LengthM = 5.20m, WidthM = 3.60m },
            new() { Name = "Sypialnia", LengthM = 3.72m, WidthM = 2.59m },
            new() { Name = "Łazienka", LengthM = 2.30m, WidthM = 1.90m }
        };

        var calculated = rooms
            .Select(r => ((IRoomAreaSource)r, AreaCalculator.Calculate(r, null, 2.60m)))
            .ToList();

        var summary = AreaCalculator.Summarize(calculated);

        var expectedFloor = calculated.Sum(x => x.Item2.FloorAreaM2 ?? 0m);
        var expectedWalls = calculated.Sum(x => x.Item2.NetWallAreaM2 ?? 0m);

        Assert.Equal(3, summary.RoomsCounted);
        Assert.Equal(expectedFloor, summary.TotalFloorAreaM2);
        Assert.Equal(expectedWalls, summary.TotalNetWallAreaM2);
        Assert.Equal(expectedWalls + expectedFloor, summary.TotalWallsAndCeilingM2);
    }

    [Fact]
    public void Summarize_EmptyList_ReturnsZeros()
    {
        var summary = AreaCalculator.Summarize([]);

        Assert.Equal(0, summary.RoomsCounted);
        Assert.Equal(0m, summary.TotalWallsAndCeilingM2);
    }
}
