using M2Manager.Api.Data;
using M2Manager.Api.Services;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Endpoints;

/// <summary>
/// Jeden zestaw endpointów CRUD dla wszystkich prostych słowników (kategorie faktur,
/// kategorie zakupów, osoby finansujące). Wcześniej każdy miał własną kopię tego samego kodu,
/// przez co poprawka w jednym — na przykład wykrywanie duplikatów bez polskich znaków —
/// nie trafiała do pozostałych.
/// </summary>
public static class LookupEndpoints
{
    public static void MapLookupEndpoints<T>(this IEndpointRouteBuilder app, string route, string label)
        where T : class, ILookupEntity, new()
    {
        var group = app.MapGroup(route).RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.Set<T>()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(items.Select(ToDto).ToList());
        });

        group.MapPost("/", async (LookupUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { message = $"Nazwa ({label}) jest wymagana." });
            }

            if (await ExistsAsync<T>(db, name, null, ct))
            {
                return Results.Conflict(new { message = $"Pozycja o tej nazwie już istnieje ({label})." });
            }

            var maxOrder = await db.Set<T>().MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;

            var entity = new T
            {
                Name = name,
                SortOrder = dto.SortOrder > 0 ? dto.SortOrder : maxOrder + 10
            };

            db.Set<T>().Add(entity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"{route}/{entity.Id}", ToDto(entity));
        });

        group.MapPut("/{id:int}", async (int id, LookupUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return Results.NotFound();
            }

            var name = dto.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { message = $"Nazwa ({label}) jest wymagana." });
            }

            if (await ExistsAsync<T>(db, name, id, ct))
            {
                return Results.Conflict(new { message = $"Pozycja o tej nazwie już istnieje ({label})." });
            }

            entity.Name = name;
            entity.SortOrder = dto.SortOrder;

            await db.SaveChangesAsync(ct);

            return Results.Ok(ToDto(entity));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var entity = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return Results.NotFound();
            }

            // Wszystkie powiązania mają OnDelete=SetNull — dokumenty i pozycje zostają, tracą tylko przypisanie.
            db.Set<T>().Remove(entity);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    /// <summary>Porównanie po normalizacji: „Ściany”, „sciany” i „ŚCIANY ” to ta sama pozycja.</summary>
    private static async Task<bool> ExistsAsync<T>(AppDbContext db, string name, int? excludeId, CancellationToken ct)
        where T : class, ILookupEntity
    {
        var normalized = TextNormalizer.Normalize(name);

        var existing = await db.Set<T>()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        return existing.Any(x => x.Id != excludeId && TextNormalizer.Normalize(x.Name) == normalized);
    }

    private static LookupDto ToDto(ILookupEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        SortOrder = entity.SortOrder
    };
}
