using M2Manager.Api.Data;
using M2Manager.Api.Services;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Endpoints;

/// <summary>Słownik sklepów i wykonawców podpowiadanych przy fakturach.</summary>
public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shops").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var shops = await db.Shops
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(shops.Select(ToDto).ToList());
        });

        group.MapPost("/", async (ShopUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var name = dto.Name.Trim();

            if (await ExistsAsync(db, name, null, ct))
            {
                return Results.Conflict(new { message = "Sklep o tej nazwie już istnieje." });
            }

            var maxOrder = await db.Shops.MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;

            var shop = new Shop
            {
                Name = name,
                Url = Clean(dto.Url),
                Notes = Clean(dto.Notes),
                SortOrder = dto.SortOrder > 0 ? dto.SortOrder : maxOrder + 10
            };

            db.Shops.Add(shop);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/shops/{shop.Id}", ToDto(shop));
        });

        group.MapPut("/{id:int}", async (int id, ShopUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (shop is null)
            {
                return Results.NotFound();
            }

            var name = dto.Name.Trim();

            if (await ExistsAsync(db, name, id, ct))
            {
                return Results.Conflict(new { message = "Sklep o tej nazwie już istnieje." });
            }

            shop.Name = name;
            shop.Url = Clean(dto.Url);
            shop.Notes = Clean(dto.Notes);
            shop.SortOrder = dto.SortOrder;

            await db.SaveChangesAsync(ct);

            return Results.Ok(ToDto(shop));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (shop is null)
            {
                return Results.NotFound();
            }

            // Sklep to tylko podpowiedź — nazwa zapisana przy fakturach zostaje nietknięta.
            db.Shops.Remove(shop);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    /// <summary>Porównanie po normalizacji, żeby „OBI ” i „obi” nie tworzyły dubli.</summary>
    private static async Task<bool> ExistsAsync(AppDbContext db, string name, int? excludeId, CancellationToken ct)
    {
        var normalized = TextNormalizer.Normalize(name);

        var shops = await db.Shops
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        return shops.Any(s => s.Id != excludeId && TextNormalizer.Normalize(s.Name) == normalized);
    }

    private static ShopDto ToDto(Shop s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Url = s.Url,
        Notes = s.Notes,
        SortOrder = s.SortOrder
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
