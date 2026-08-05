using ClosedXML.Excel;
using M2Manager.Api;
using M2Manager.Api.Endpoints;
using M2Manager.Api.Services;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;

namespace M2Manager.Tests;

public class ShoppingSummaryBuilderTests
{
    private static ShoppingItemDto Item(
        string name,
        string room = "Salon",
        string? category = "Ściany",
        decimal? total = null,
        decimal? planned = null,
        decimal? actual = null,
        ShoppingStatus status = ShoppingStatus.ToBuy) => new()
    {
        Name = name,
        RoomName = room,
        CategoryName = category,
        TotalCost = total,
        PlannedBudget = planned,
        ActualCost = actual,
        Status = status
    };

    [Fact]
    public void Build_SumsCostsBudgetAndActual()
    {
        var items = new List<ShoppingItemDto>
        {
            Item("Farba", total: 300m, planned: 350m, actual: 289.99m),
            Item("Wałek", total: 45.50m, planned: 40m, actual: 42m)
        };

        var summary = ShoppingSummaryBuilder.Build(1, items);

        Assert.Equal(345.50m, summary.TotalCost);
        Assert.Equal(390m, summary.PlannedBudget);
        Assert.Equal(331.99m, summary.ActualCost);
        Assert.Equal(58.01m, summary.BudgetDifference);
    }

    [Fact]
    public void Build_ProgressCountsBoughtAndInstalled()
    {
        var items = new List<ShoppingItemDto>
        {
            Item("A", status: ShoppingStatus.ToBuy),
            Item("B", status: ShoppingStatus.Ordered),
            Item("C", status: ShoppingStatus.Bought),
            Item("D", status: ShoppingStatus.Installed)
        };

        var summary = ShoppingSummaryBuilder.Build(1, items);

        Assert.Equal(2, summary.DoneCount);
        Assert.Equal(50m, summary.ProgressPercent);
    }

    [Fact]
    public void Build_CancelledItems_DoNotCountTowardsBudgetOrProgress()
    {
        var items = new List<ShoppingItemDto>
        {
            Item("Kupione", total: 100m, status: ShoppingStatus.Bought),
            Item("Porzucone", total: 5000m, planned: 5000m, status: ShoppingStatus.Cancelled)
        };

        var summary = ShoppingSummaryBuilder.Build(1, items);

        Assert.Equal(100m, summary.TotalCost);
        Assert.Equal(0m, summary.PlannedBudget);
        Assert.Equal(100m, summary.ProgressPercent);

        // W liczniku wszystkich pozycji porzucone nadal widać — to informacja, nie śmieć.
        Assert.Equal(2, summary.ItemsCount);
    }

    [Fact]
    public void Build_GroupsByRoomAndCategory()
    {
        var items = new List<ShoppingItemDto>
        {
            Item("Farba", room: "Salon", category: "Ściany", total: 300m),
            Item("Płytki", room: "Łazienka", category: "Płytki", total: 900m),
            Item("Grunt", room: "Salon", category: "Ściany", total: 100m)
        };

        var summary = ShoppingSummaryBuilder.Build(1, items);

        var salon = summary.ByRoom.Single(g => g.Key == "Salon");
        Assert.Equal(2, salon.ItemsCount);
        Assert.Equal(400m, salon.TotalCost);

        // Grupy są posortowane malejąco po koszcie — najdroższe pierwsze.
        Assert.Equal("Łazienka", summary.ByRoom.First().Key);
        Assert.Equal(2, summary.ByCategory.Count);
    }

    [Fact]
    public void Build_EmptyList_ReturnsZeroProgressWithoutDividingByZero()
    {
        var summary = ShoppingSummaryBuilder.Build(1, []);

        Assert.Equal(0, summary.ItemsCount);
        Assert.Equal(0m, summary.ProgressPercent);
        Assert.Empty(summary.ByRoom);
    }
}

public class ShoppingSortingTests
{
    private static List<ShoppingItemDto> Sample() =>
    [
        new() { Id = 1, Name = "Zlew", OrdinalNo = 3, RoomName = "Kuchnia", TotalCost = 500m, Status = ShoppingStatus.Bought },
        new() { Id = 2, Name = "Farba", OrdinalNo = 1, RoomName = ShoppingConstants.WholePropertyRoomName, TotalCost = 300m },
        new() { Id = 3, Name = "Kafelki", OrdinalNo = 2, RoomName = "Łazienka", TotalCost = 900m }
    ];

    [Fact]
    public void SortItems_DefaultGroupsByRoom_WithWholePropertyLast()
    {
        var sorted = ShoppingEndpoints.SortItems(Sample(), null);

        Assert.Equal(["Kuchnia", "Łazienka", ShoppingConstants.WholePropertyRoomName],
            sorted.Select(i => i.RoomName));
    }

    [Fact]
    public void SortItems_ByCostDescending()
    {
        var sorted = ShoppingEndpoints.SortItems(Sample(), "-cost");

        Assert.Equal([900m, 500m, 300m], sorted.Select(i => i.TotalCost));
    }

    [Fact]
    public void SortItems_ByOrdinalAscending()
    {
        var sorted = ShoppingEndpoints.SortItems(Sample(), "ordinal");

        Assert.Equal([1, 2, 3], sorted.Select(i => i.OrdinalNo));
    }

    [Fact]
    public void SortItems_UnknownKey_FallsBackToDefault()
    {
        var sorted = ShoppingEndpoints.SortItems(Sample(), "cokolwiek");

        Assert.Equal("Kuchnia", sorted[0].RoomName);
    }
}

public class ShoppingImportParsingTests
{
    [Theory]
    [InlineData("~Koszt szt.", "kosztszt")]
    [InlineData("Wykonawca/sklep", "wykonawcasklep")]
    [InlineData("Uwagi/obliczenia", "uwagiobliczenia")]
    [InlineData("Ilość", "ilosc")]
    [InlineData("Planowany budżet (z amortyzacją)", "planowanybudzetzamortyzacja")]
    [InlineData("  L.p  ", "lp")]
    public void Normalize_StripsDiacriticsAndPunctuation(string raw, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(raw));
    }

    /// <summary>Modele potrafią gubić polskie znaki — dopasowanie kategorii musi to znieść.</summary>
    [Theory]
    [InlineData("Wyposażenie", "Wyposazenie")]
    [InlineData("Wyposażenie", "wyposażenie")]
    [InlineData("Remont i materiały", "Remont i materialy")]
    [InlineData("Media (prąd, gaz, woda)", "Media (prad, gaz, woda)")]
    public void Normalize_MakesCategoryNamesComparable(string fromDatabase, string fromModel)
    {
        Assert.Equal(TextNormalizer.Normalize(fromDatabase), TextNormalizer.Normalize(fromModel));
    }

    [Fact]
    public void Normalize_DifferentCategories_StayDifferent()
    {
        Assert.NotEqual(TextNormalizer.Normalize("Wyposażenie"), TextNormalizer.Normalize("Ubezpieczenie"));
    }

    [Theory]
    [InlineData("Kupione", ShoppingStatus.Bought)]
    [InlineData("zamówione", ShoppingStatus.Ordered)]
    [InlineData("Zamontowane", ShoppingStatus.Installed)]
    [InlineData("zrezygnowano", ShoppingStatus.Cancelled)]
    [InlineData("", ShoppingStatus.ToBuy)]
    [InlineData(null, ShoppingStatus.ToBuy)]
    [InlineData("cokolwiek", ShoppingStatus.ToBuy)]
    public void ParseStatus_MapsPolishLabels(string? raw, ShoppingStatus expected)
    {
        Assert.Equal(expected, ShoppingImportService.ParseStatus(raw));
    }

    [Fact]
    public void ParsePriority_QuestionMarkInNameMeansNiceToHave()
    {
        Assert.Equal(ShoppingPriority.NiceToHave,
            ShoppingImportService.ParsePriority(null, "Wieszak na ręczniki?"));

        Assert.Equal(ShoppingPriority.MustHave,
            ShoppingImportService.ParsePriority(null, "Farba do sypialni"));
    }

    [Fact]
    public void ParsePriority_ExplicitColumnWinsOverHeuristic()
    {
        Assert.Equal(ShoppingPriority.Optional,
            ShoppingImportService.ParsePriority("Opcjonalne", "Wieszak?"));
    }

    [Fact]
    public void FindHeaderRow_LocatesHeadersEvenBelowTitleRows()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Arkusz1");

        ws.Cell(1, 1).Value = "Lista zakupów — mieszkanie parter";
        ws.Cell(2, 1).Value = "aktualizacja 2026-05";

        string[] headers = ["L.p", "Pomieszczenie", "Kategoria", "Pozycja", "Ilość", "~Koszt szt.", "Link"];
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(4, i + 1).Value = headers[i];
        }

        ws.Cell(5, 4).Value = "Farba";

        var (headerRow, columns) = ShoppingImportService.FindHeaderRow(ws);

        Assert.Equal(4, headerRow);
        Assert.Equal(7, columns.Count);
        Assert.Contains(columns, c => c.Value == ShoppingColumn.Name && c.Key == 4);
        Assert.Contains(columns, c => c.Value == ShoppingColumn.UnitCost && c.Key == 6);
    }

    [Fact]
    public void FindHeaderRow_SheetWithoutHeaders_ReturnsZero()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Arkusz1");
        ws.Cell(1, 1).Value = "jakiś tekst";

        var (headerRow, columns) = ShoppingImportService.FindHeaderRow(ws);

        Assert.Equal(0, headerRow);
        Assert.Empty(columns);
    }
}

public class ShoppingMappingTests
{
    [Fact]
    public void ResolveTotalCost_ComputesFromQuantityAndUnitCost()
    {
        var dto = new ShoppingItemUpsertDto { Quantity = 3m, UnitCost = 24.99m };

        Assert.Equal(74.97m, Mapping.ResolveTotalCost(dto));
    }

    [Fact]
    public void ResolveTotalCost_ManualValueWins()
    {
        var dto = new ShoppingItemUpsertDto { Quantity = 3m, UnitCost = 10m, TotalCost = 25m };

        Assert.Equal(25m, Mapping.ResolveTotalCost(dto));
    }

    [Fact]
    public void ResolveTotalCost_MissingInputs_ReturnsNull()
    {
        Assert.Null(Mapping.ResolveTotalCost(new ShoppingItemUpsertDto { Quantity = 3m }));
        Assert.Null(Mapping.ResolveTotalCost(new ShoppingItemUpsertDto()));
    }
}
