using M2Manager.Shared;
using M2Manager.Shared.Areas;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Data;

/// <summary>
/// Dane startowe. Każdy blok jest idempotentny — dokłada tylko to, czego brakuje,
/// więc ponowny start aplikacji nie duplikuje słowników.
/// </summary>
public static class DbSeeder
{
    private static readonly string[] ExpenseCategoryNames =
    [
        "Media (prąd, gaz, woda)",
        "Internet / telewizja",
        "Czynsz wspólnoty / spółdzielni",
        "Remont i materiały",
        "Wyposażenie",
        "Naprawy i serwis",
        "Podatki i opłaty urzędowe",
        "Ubezpieczenie",
        "Rata kredytu / odsetki",
        "Inne"
    ];

    private static readonly string[] ShoppingCategoryNames =
    [
        "Ściany", "Podłogi", "Płytki", "Ceramika", "Armatura", "Prysznic", "Meble",
        "Wyposażenie", "AGD", "Chemia", "Elektryka", "Drzwi", "Narzędzia", "Rośliny"
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await SeedExpenseCategoriesAsync(db, ct);
        await SeedShoppingCategoriesAsync(db, ct);
        await SeedPropertiesAsync(db, ct);
    }

    private static async Task SeedExpenseCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.ExpenseCategories
            .Select(c => c.Name)
            .ToListAsync(ct);

        var order = 0;
        foreach (var name in ExpenseCategoryNames)
        {
            order += 10;
            if (existing.Contains(name))
            {
                continue;
            }

            db.ExpenseCategories.Add(new ExpenseCategory { Name = name, SortOrder = order });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedShoppingCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.ShoppingCategories
            .Select(c => c.Name)
            .ToListAsync(ct);

        var order = 0;
        foreach (var name in ShoppingCategoryNames)
        {
            order += 10;
            if (existing.Contains(name))
            {
                continue;
            }

            db.ShoppingCategories.Add(new ShoppingCategory { Name = name, SortOrder = order });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Mieszkania tworzymy tylko wtedy, gdy baza jest pusta — nie chcemy nadpisywać pracy użytkownika.</summary>
    private static async Task SeedPropertiesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Properties.AnyAsync(ct))
        {
            return;
        }

        var own = new Property
        {
            Name = "Mieszkanie własne",
            Purpose = PropertyPurpose.OwnOccupied,
            DefaultRoomHeightM = AreaCalculator.FallbackRoomHeightM,
            Rooms = BuildSampleRooms()
        };

        var rental = new Property
        {
            Name = "Mieszkanie na wynajem",
            Purpose = PropertyPurpose.Rental,
            DefaultRoomHeightM = AreaCalculator.FallbackRoomHeightM
        };

        db.Properties.AddRange(own, rental);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Przykładowe pomieszczenia z gotową geometrią, żeby edytor rzutu nie startował z pustą kartką.
    /// Sypialnia celowo odwzorowuje przykład referencyjny (3,72 × 2,59 m, drzwi 90×200, okno 60×90 → 30,47 m² ścian netto).
    /// </summary>
    private static List<Room> BuildSampleRooms() =>
    [
        new Room
        {
            Name = "Salon (aneks kuchenny)",
            SortOrder = 10,
            LengthM = 5.20m,
            WidthM = 3.60m,
            GeometryJson = Rect(0, 0, 520, 360),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Window, WidthCm = 180, HeightCm = 220, WallSide = WallSide.South, OffsetCm = 120 },
                new RoomOpening { Type = OpeningType.Window, WidthCm = 120, HeightCm = 150, WallSide = WallSide.West, OffsetCm = 80 },
                new RoomOpening { Type = OpeningType.Doorway, WidthCm = 90, HeightCm = 200, WallSide = WallSide.East, OffsetCm = 60 }
            ]
        },
        new Room
        {
            Name = "Sypialnia",
            SortOrder = 20,
            LengthM = 3.72m,
            WidthM = 2.59m,
            GeometryJson = Rect(540, 0, 372, 259),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Door, WidthCm = 90, HeightCm = 200, WallSide = WallSide.West, OffsetCm = 40 },
                new RoomOpening { Type = OpeningType.Window, WidthCm = 60, HeightCm = 90, WallSide = WallSide.North, OffsetCm = 150 }
            ]
        },
        new Room
        {
            Name = "Łazienka",
            SortOrder = 30,
            LengthM = 2.30m,
            WidthM = 1.90m,
            GeometryJson = Rect(540, 280, 230, 190),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Door, WidthCm = 80, HeightCm = 200, WallSide = WallSide.West, OffsetCm = 30 }
            ]
        },
        new Room
        {
            Name = "Przedpokój",
            SortOrder = 40,
            LengthM = 2.60m,
            WidthM = 1.40m,
            GeometryJson = Rect(0, 380, 260, 140),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Door, WidthCm = 90, HeightCm = 200, WallSide = WallSide.South, OffsetCm = 60 }
            ]
        },
        new Room
        {
            Name = "Ogródek",
            SortOrder = 50,
            LengthM = 6.00m,
            WidthM = 4.00m,
            IncludeInTotals = false,
            Notes = "Wyłączony z sum — nie ma ścian ani sufitu do malowania.",
            GeometryJson = Rect(0, 560, 600, 400)
        }
    ];

    private static string Rect(decimal x, decimal y, decimal widthCm, decimal heightCm) =>
        new RoomGeometry { X = x, Y = y, WidthCm = widthCm, HeightCm = heightCm }.ToJson();
}
