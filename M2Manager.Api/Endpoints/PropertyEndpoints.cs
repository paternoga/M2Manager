using M2Manager.Api.Data;
using M2Manager.Shared.Areas;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Endpoints;

/// <summary>Moduł 2: mieszkania, pomieszczenia, otwory, wyliczenia powierzchni i zapis rzutu.</summary>
public static class PropertyEndpoints
{
    public static void MapPropertyEndpoints(this IEndpointRouteBuilder app)
    {
        MapProperties(app);
        MapRooms(app);
        MapOpenings(app);
    }

    // ---------------------------------------------------------------- mieszkania

    private static void MapProperties(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/properties").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var properties = await db.Properties
                .Include(p => p.Rooms)
                .OrderBy(p => p.Id)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(properties.Select(p => p.ToDto()).ToList());
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var property = await db.Properties
                .Include(p => p.Rooms)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            return property is null ? Results.NotFound() : Results.Ok(property.ToDto());
        });

        group.MapPost("/", async (PropertyUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var property = new Property();
            dto.ApplyTo(property);

            db.Properties.Add(property);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/properties/{property.Id}", property.ToDto());
        });

        group.MapPut("/{id:int}", async (int id, PropertyUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var property = await db.Properties
                .Include(p => p.Rooms)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (property is null)
            {
                return Results.NotFound();
            }

            dto.ApplyTo(property);
            await db.SaveChangesAsync(ct);

            return Results.Ok(property.ToDto());
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (property is null)
            {
                return Results.NotFound();
            }

            db.Properties.Remove(property);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        // Wyliczone powierzchnie dla całego mieszkania.
        group.MapGet("/{id:int}/areas", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var property = await db.Properties
                .Include(p => p.Rooms.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
                .ThenInclude(r => r.Openings)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            return property is null ? Results.NotFound() : Results.Ok(BuildAreas(property));
        });

        // Batchowy zapis geometrii z edytora rzutu.
        group.MapPut("/{id:int}/layout", async (
            int id,
            LayoutSaveRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var property = await db.Properties
                .Include(p => p.Rooms)
                .ThenInclude(r => r.Openings)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (property is null)
            {
                return Results.NotFound();
            }

            var roomsById = property.Rooms.ToDictionary(r => r.Id);

            foreach (var layout in request.Rooms)
            {
                if (!roomsById.TryGetValue(layout.RoomId, out var room))
                {
                    continue;
                }

                room.GeometryJson = string.IsNullOrWhiteSpace(layout.GeometryJson) ? null : layout.GeometryJson;

                // Edytor po przeciągnięciu odsyła też wymiary — pola liczbowe mają się zgadzać z rysunkiem.
                if (layout.LengthM.HasValue)
                {
                    room.LengthM = layout.LengthM;
                }

                if (layout.WidthM.HasValue)
                {
                    room.WidthM = layout.WidthM;
                }

                if (layout.FloorAreaM2.HasValue)
                {
                    room.FloorAreaM2 = layout.FloorAreaM2;
                }
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(BuildAreas(property));
        });
    }

    // ---------------------------------------------------------------- pomieszczenia

    private static void MapRooms(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/properties/{propertyId:int}/rooms").RequireAuthorization();

        group.MapGet("/", async (int propertyId, AppDbContext db, CancellationToken ct) =>
        {
            var exists = await db.Properties.AnyAsync(p => p.Id == propertyId, ct);
            if (!exists)
            {
                return Results.NotFound();
            }

            var rooms = await db.Rooms
                .Where(r => r.PropertyId == propertyId)
                .Include(r => r.Openings)
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Id)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(rooms.Select(r => r.ToDto()).ToList());
        });

        group.MapPost("/", async (int propertyId, RoomUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            if (!await db.Properties.AnyAsync(p => p.Id == propertyId, ct))
            {
                return Results.NotFound();
            }

            var room = new Room { PropertyId = propertyId };
            dto.ApplyTo(room);

            if (room.SortOrder == 0)
            {
                var maxOrder = await db.Rooms
                    .Where(r => r.PropertyId == propertyId)
                    .MaxAsync(r => (int?)r.SortOrder, ct) ?? 0;

                room.SortOrder = maxOrder + 10;
            }

            db.Rooms.Add(room);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/properties/{propertyId}/rooms/{room.Id}", room.ToDto());
        });

        group.MapPut("/{roomId:int}", async (
            int propertyId,
            int roomId,
            RoomUpsertDto dto,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var room = await db.Rooms
                .Include(r => r.Openings)
                .FirstOrDefaultAsync(r => r.Id == roomId && r.PropertyId == propertyId, ct);

            if (room is null)
            {
                return Results.NotFound();
            }

            dto.ApplyTo(room);
            await db.SaveChangesAsync(ct);

            return Results.Ok(room.ToDto());
        });

        group.MapDelete("/{roomId:int}", async (int propertyId, int roomId, AppDbContext db, CancellationToken ct) =>
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.PropertyId == propertyId, ct);
            if (room is null)
            {
                return Results.NotFound();
            }

            db.Rooms.Remove(room);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    // ---------------------------------------------------------------- okna i drzwi

    private static void MapOpenings(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms/{roomId:int}/openings").RequireAuthorization();

        group.MapGet("/", async (int roomId, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            {
                return Results.NotFound();
            }

            var openings = await db.RoomOpenings
                .Where(o => o.RoomId == roomId)
                .OrderBy(o => o.Id)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(openings.Select(o => o.ToDto()).ToList());
        });

        group.MapPost("/", async (int roomId, RoomOpeningUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
            {
                return Results.NotFound();
            }

            var opening = new RoomOpening { RoomId = roomId };
            dto.ApplyTo(opening);

            db.RoomOpenings.Add(opening);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/rooms/{roomId}/openings/{opening.Id}", opening.ToDto());
        });

        group.MapPut("/{openingId:int}", async (
            int roomId,
            int openingId,
            RoomOpeningUpsertDto dto,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var opening = await db.RoomOpenings
                .FirstOrDefaultAsync(o => o.Id == openingId && o.RoomId == roomId, ct);

            if (opening is null)
            {
                return Results.NotFound();
            }

            dto.ApplyTo(opening);
            await db.SaveChangesAsync(ct);

            return Results.Ok(opening.ToDto());
        });

        group.MapDelete("/{openingId:int}", async (int roomId, int openingId, AppDbContext db, CancellationToken ct) =>
        {
            var opening = await db.RoomOpenings
                .FirstOrDefaultAsync(o => o.Id == openingId && o.RoomId == roomId, ct);

            if (opening is null)
            {
                return Results.NotFound();
            }

            db.RoomOpenings.Remove(opening);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    /// <summary>Liczy powierzchnie dla wszystkich pomieszczeń mieszkania plus podsumowanie.</summary>
    internal static PropertyAreasDto BuildAreas(Property property)
    {
        var rows = new List<RoomAreaDto>();
        var forSummary = new List<(IRoomAreaSource Room, RoomAreaResult Result)>();

        foreach (var room in property.Rooms.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
        {
            var result = AreaCalculator.Calculate(room, room.Openings, property.DefaultRoomHeightM);

            rows.Add(new RoomAreaDto
            {
                RoomId = room.Id,
                RoomName = room.Name,
                IncludeInTotals = room.IncludeInTotals,
                Area = result
            });

            forSummary.Add((room, result));
        }

        return new PropertyAreasDto
        {
            PropertyId = property.Id,
            PropertyName = property.Name,
            DefaultRoomHeightM = property.DefaultRoomHeightM,
            Rooms = rows,
            Summary = AreaCalculator.Summarize(forSummary)
        };
    }
}
