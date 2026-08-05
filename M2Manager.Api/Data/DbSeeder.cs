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
    /// <summary>
    /// Kategorie faktur. Kolejność jest celowa — od opłat cyklicznych, przez remont,
    /// po formalności. Pozycje oznaczone komentarzem to propozycje wykraczające poza
    /// pierwotną listę; przy staraniu się o obniżenie marży kredytu bank i księgowa
    /// wolą widzieć rozbicie niż jeden worek „remont”.
    /// </summary>
    private static readonly string[] ExpenseCategoryNames =
    [
        // remont — na czas trwania prac to główny worek kosztów, więc idzie na górę listy
        "Remont i materiały",
        "Robocizna / usługi remontowe",   // rozdzielenie materiału od pracy — kluczowe w rozliczeniu
        "Meble i AGD",                    // duże zakupy warto oddzielić od drobnego wyposażenia
        "Wyposażenie",
        "Transport i dostawa",            // przy remoncie potrafi urosnąć do realnej kwoty
        "Wywóz odpadów",                  // kontener na gruz, poza zwykłą opłatą śmieciową

        // opłaty bieżące
        "Media (prąd, gaz, woda)",
        "Internet / telewizja",
        "Czynsz wspólnoty / spółdzielni",
        "Fundusz remontowy",              // zwykle osobna pozycja na czynszu — warto śledzić oddzielnie

        // utrzymanie
        "Naprawy i serwis",
        "Przeglądy i konserwacja",        // obowiązkowe przeglądy gazowe, kominiarskie, elektryczne

        // finanse i formalności
        "Rata kredytu / odsetki",
        "Ubezpieczenie",
        "Podatki i opłaty urzędowe",
        "Zarządzanie najmem",             // pośrednik, zarządca, ogłoszenia — dotyczy mieszkania na wynajem
        "Inne"
    ];

    /// <summary>
    /// Kategorie listy zakupów. Trzon pochodzi z arkusza prowadzonego ręcznie,
    /// reszta to propozycje wypełniające widoczne luki (m.in. robocizna, oświetlenie,
    /// okna, tekstylia) — układ idzie od stanu surowego do wykończenia.
    /// </summary>
    private static readonly string[] ShoppingCategoryNames =
    [
        // powierzchnie
        "Ściany",
        "Sufity",                 // osobno od ścian — inna farba, inna wydajność
        "Podłogi",
        "Płytki",

        // instalacje i łazienka
        "Ceramika",
        "Armatura",
        "Hydraulika",             // rury, zawory, syfony — to nie to samo co armatura
        "Prysznic",
        "Ogrzewanie",             // ogrzewanie podłogowe, grzejniki, sterowniki
        "Elektryka",
        "Oświetlenie",            // lampy, plafony, LED-y — w arkuszu tonęły w „Elektryka”

        // stolarka
        "Drzwi",
        "Okna i parapety",

        // umeblowanie i sprzęt
        "Meble",
        "Wyposażenie",
        "AGD",
        "Tekstylia",              // zasłony, rolety, dywany, pościel
        "Dekoracje",

        // narzędzia i organizacja remontu
        "Chemia",
        "Narzędzia",
        "Usługi / robocizna",     // glazurnik, elektryk, hydraulik — bez tego budżet remontu jest fikcją
        "Sprzątanie i odpady",    // kontener, worki, sprzątanie poremontowe

        // na zewnątrz
        "Balkon i ogród",
        "Rośliny"
    ];

    /// <summary>
    /// Sklepy i wykonawcy podpowiadani przy fakturach. Lista startowa pod remont —
    /// resztę dopiszesz sam w Ustawieniach, a te, których nie używasz, po prostu usuniesz.
    /// </summary>
    private static readonly string[] ShopNames =
    [
        // markety budowlane
        "Leroy Merlin", "Castorama", "OBI", "Bricomarché", "PSB Mrówka", "Merkury Market", "Bricoman",

        // wykończenie i podłogi
        "Komfort",

        // meble
        "IKEA", "Agata Meble", "Black Red White", "JYSK",

        // AGD i RTV
        "Media Expert", "RTV Euro AGD", "Media Markt",

        // zakupy online
        "Allegro", "OLX", "Amazon",

        // wykonawcy — pod kategorię „Usługi / robocizna”
        "Glazurnik", "Hydraulik", "Elektryk", "Malarz", "Stolarz",

        "Inny sklep"
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await SeedExpenseCategoriesAsync(db, ct);
        await SeedShoppingCategoriesAsync(db, ct);
        await SeedShopsAsync(db, ct);
        await SeedPropertiesAsync(db, ct);
    }

    /// <summary>
    /// Sklepy dokładamy tylko wtedy, gdy tabela jest pusta. Inaczej usunięty przez użytkownika
    /// sklep wracałby przy każdym starcie aplikacji.
    /// </summary>
    private static async Task SeedShopsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Shops.AnyAsync(ct))
        {
            return;
        }

        var order = 0;
        foreach (var name in ShopNames)
        {
            order += 10;
            db.Shops.Add(new Shop { Name = name, SortOrder = order });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedExpenseCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.ExpenseCategories.ToDictionaryAsync(c => c.Name, c => c, ct);

        var order = 0;
        foreach (var name in ExpenseCategoryNames)
        {
            order += 10;

            if (existing.TryGetValue(name, out var category))
            {
                // Porządkujemy również kategorie, które już były w bazie. Bez tego po dołożeniu
                // nowych pozycji lista w UI układa się w kolejności historycznej zamiast logicznej
                // i np. „Inne” ląduje w środku.
                category.SortOrder = order;
                continue;
            }

            db.ExpenseCategories.Add(new ExpenseCategory { Name = name, SortOrder = order });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedShoppingCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.ShoppingCategories.ToDictionaryAsync(c => c.Name, c => c, ct);

        var order = 0;
        foreach (var name in ShoppingCategoryNames)
        {
            order += 10;

            if (existing.TryGetValue(name, out var category))
            {
                category.SortOrder = order;
                continue;
            }

            db.ShoppingCategories.Add(new ShoppingCategory { Name = name, SortOrder = order });
        }

        // Kategorie dodane ręcznie przez użytkownika zostawiamy w spokoju — ich SortOrder
        // pochodzi z API i nie chcemy go nadpisywać przy każdym starcie.
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
            Name = "Mieszkanie własne (parter)",
            Purpose = PropertyPurpose.OwnOccupied,
            TotalAreaM2 = 37.12m,
            DefaultRoomHeightM = AreaCalculator.FallbackRoomHeightM,
            Rooms = BuildGroundFloorRooms()
        };

        var rental = new Property
        {
            Name = "Mieszkanie na wynajem (piętro)",
            Purpose = PropertyPurpose.Rental,
            DefaultRoomHeightM = AreaCalculator.FallbackRoomHeightM,
            Rooms = BuildUpstairsRooms()
        };

        db.Properties.AddRange(own, rental);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Układ parteru odwzorowany z arkusza „Mieszkanie parter 38m2”: numeracja pomieszczeń,
    /// metraże z pomiaru i wyłączona z malowania ściana aneksu.
    ///
    /// Sypialnia ma wymiary potwierdzone (3,72 × 2,59 m, drzwi 90×200, okno 60×90), więc wychodzi
    /// z niej dokładnie 30,47 m² ścian netto — tak samo jak liczone ręcznie w arkuszu.
    /// Tam, gdzie znany jest tylko metraż, `FloorAreaM2` jest wpisany wprost i nadpisuje
    /// wyliczenie z długości i szerokości, a same wymiary są orientacyjne (opisane w notatce).
    /// </summary>
    private static List<Room> BuildGroundFloorRooms() =>
    [
        new Room
        {
            // Nazwy celowo takie, jak w arkuszu zakupów — inaczej import nie dopasuje
            // pomieszczeń i założy duplikaty obok istniejących.
            Name = "Salon",
            SortOrder = 10,
            FloorAreaM2 = 23.18m,
            LengthM = 5.90m,
            WidthM = 3.93m,

            // Ściana między aneksem a salonem (259 × 260 cm) nie idzie do malowania.
            ExcludedWallAreaM2 = 6.70m,
            Notes = "Metraż 23,18 m² z pomiaru, wymiary orientacyjne — popraw po zmierzeniu. "
                    + "Ściana aneksu (259×260 cm ≈ 6,7 m²) wyłączona z malowania.",
            GeometryJson = Rect(0, 0, 590, 393),
            Openings =
            [
                new RoomOpening
                {
                    Type = OpeningType.Door, WidthCm = 90, HeightCm = 220,
                    WallSide = WallSide.South, OffsetCm = 200, Notes = "Wyjście na ogród"
                }
            ]
        },
        new Room
        {
            Name = "Sypialnia",
            SortOrder = 30,

            // Metraż z arkusza (9,31 m²) jest mniejszy niż 3,72 × 2,59 = 9,63 — pomieszczenie
            // nie jest idealnym prostokątem. Wymiary służą do obwodu, metraż wpisujemy wprost.
            FloorAreaM2 = 9.31m,
            LengthM = 3.72m,
            WidthM = 2.59m,
            Notes = "Wymiary potwierdzone pomiarem; metraż 9,31 m² z arkusza.",
            GeometryJson = Rect(610, 0, 372, 259),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Door, WidthCm = 90, HeightCm = 200, WallSide = WallSide.West, OffsetCm = 40 },
                new RoomOpening { Type = OpeningType.Window, WidthCm = 60, HeightCm = 90, WallSide = WallSide.North, OffsetCm = 150 }
            ]
        },
        new Room
        {
            Name = "Łazienka",
            SortOrder = 40,
            FloorAreaM2 = 4.63m,
            LengthM = 2.30m,
            WidthM = 2.01m,
            Notes = "Metraż 4,63 m² z pomiaru, wymiary orientacyjne.",
            GeometryJson = Rect(610, 280, 230, 201),
            Openings =
            [
                new RoomOpening { Type = OpeningType.Door, WidthCm = 80, HeightCm = 200, WallSide = WallSide.West, OffsetCm = 30 }
            ]
        },
        new Room
        {
            // Aneks kuchenny jest częścią salonu, ale na liście zakupów ma własne pozycje
            // (meble, blat, zlew, AGD), więc musi istnieć jako osobne pomieszczenie.
            // Bez metrażu — jego podłoga i ściany liczą się już w salonie.
            Name = "Kuchnia",
            SortOrder = 20,
            ManualWallAreaM2 = 0m,
            ManualCeilingAreaM2 = 0m,
            Notes = "Aneks kuchenny — powierzchnia liczona w salonie, żeby nie dublować metrażu."
        },
        new Room
        {
            // W arkuszu powierzchni figuruje jako „Wnęka wejściowa”, na liście zakupów jako „Przedpokój”.
            Name = "Przedpokój",
            SortOrder = 50,

            // Wnosi wyłącznie ściany — podłoga mieści się już w metrażu salonu, dlatego
            // celowo nie ma wymiarów ani geometrii: inaczej doliczyłaby metraż drugi raz.
            ManualWallAreaM2 = 9.50m,
            ManualCeilingAreaM2 = 0m,
            Notes = "Wnęka wejściowa — tylko ściany (9,5 m²), metraż podłogi liczy się w salonie."
        },
        new Room
        {
            Name = "Ogródek",
            SortOrder = 60,
            FloorAreaM2 = 30.00m,
            LengthM = 6.00m,
            WidthM = 5.00m,
            IncludeInTotals = false,
            Notes = "Wyłączony z sum — nie ma ścian ani sufitu do malowania.",
            GeometryJson = Rect(0, 560, 600, 500)
        }
    ];

    /// <summary>
    /// Mieszkanie na piętrze. Znamy układ pomieszczeń, ale nie ich wymiary — dlatego
    /// celowo nie ma tu żadnych metraży ani geometrii. Wpisanie zmyślonych liczb byłoby gorsze
    /// niż ich brak: sumy do malowania wyglądałyby wiarygodnie i byłyby nieprawdziwe.
    /// Wymiary uzupełnisz w formularzu albo rysując pomieszczenia w edytorze rzutu.
    /// </summary>
    private static List<Room> BuildUpstairsRooms()
    {
        string[] names = ["Kuchnia", "Salon", "Sypialnia", "Łazienka", "Hol"];

        return names
            .Select((name, index) => new Room
            {
                Name = name,
                SortOrder = (index + 1) * 10,
                Notes = "Brak wymiarów — uzupełnij długość i szerokość albo narysuj pomieszczenie w edytorze rzutu."
            })
            .ToList();
    }

    private static string Rect(decimal x, decimal y, decimal widthCm, decimal heightCm) =>
        new RoomGeometry { X = x, Y = y, WidthCm = widthCm, HeightCm = heightCm }.ToJson();
}
