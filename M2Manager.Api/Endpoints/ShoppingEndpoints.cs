using M2Manager.Api.Data;
using M2Manager.Api.Services;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace M2Manager.Api.Endpoints;

/// <summary>Moduł 3: lista rzeczy do zakupu — CRUD, sumy, import z arkusza i eksporty.</summary>
public static class ShoppingEndpoints
{
    public static void MapShoppingEndpoints(this IEndpointRouteBuilder app)
    {
        MapItems(app);
    }

    private static void MapItems(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopping").RequireAuthorization();

        // ---------- lista z filtrami i sortowaniem ----------
        group.MapGet("/", async (
            int? propertyId,
            int? roomId,
            int? categoryId,
            int? payerId,
            ShoppingStatus? status,
            ShoppingPriority? priority,
            string? search,
            string? sort,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var items = await LoadItemsAsync(db, propertyId, roomId, categoryId, payerId, status, priority, search, ct);
            return Results.Ok(SortItems(items, sort));
        });

        // ---------- sumy ----------
        group.MapGet("/summary", async (int? propertyId, AppDbContext db, CancellationToken ct) =>
        {
            var items = await LoadItemsAsync(db, propertyId, null, null, null, null, null, null, ct);
            return Results.Ok(ShoppingSummaryBuilder.Build(propertyId ?? 0, items));
        });

        // ---------- CRUD ----------
        group.MapPost("/", async (ShoppingItemUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            if (await ValidateReferencesAsync(db, dto, ct) is { } referenceError)
            {
                return referenceError;
            }

            var item = new ShoppingItem();
            dto.ApplyTo(item);

            item.OrdinalNo = dto.OrdinalNo ?? await NextOrdinalAsync(db, dto.PropertyId, ct);

            db.ShoppingItems.Add(item);
            await db.SaveChangesAsync(ct);

            var created = await LoadSingleAsync(db, item.Id, ct);
            return Results.Created($"/api/shopping/{item.Id}", created);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var item = await LoadSingleAsync(db, id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPut("/{id:int}", async (int id, ShoppingItemUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var item = await db.ShoppingItems.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            if (await ValidateReferencesAsync(db, dto, ct) is { } referenceError)
            {
                return referenceError;
            }

            dto.ApplyTo(item);

            if (dto.OrdinalNo.HasValue)
            {
                item.OrdinalNo = dto.OrdinalNo.Value;
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadSingleAsync(db, id, ct));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var item = await db.ShoppingItems.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            db.ShoppingItems.Remove(item);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        // ---------- import z arkusza ----------
        group.MapPost("/import", async (
                HttpRequest http,
                AppDbContext db,
                ShoppingImportService importer,
                CancellationToken ct) =>
            {
                if (!http.HasFormContentType)
                {
                    return Results.BadRequest(new { message = "Oczekiwano formularza multipart z plikiem .xlsx." });
                }

                var form = await http.ReadFormAsync(ct);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { message = "Nie przesłano pliku." });
                }

                if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { message = "Obsługiwany jest wyłącznie format .xlsx." });
                }

                var propertyId = int.TryParse(form["propertyId"], out var parsed) ? parsed : 0;
                if (propertyId <= 0 || !await db.Properties.AnyAsync(p => p.Id == propertyId, ct))
                {
                    return Results.BadRequest(new { message = "Wskaż mieszkanie, do którego importujemy listę." });
                }

                await using var stream = file.OpenReadStream();

                try
                {
                    var result = await importer.ImportAsync(stream, propertyId, ct);
                    return Results.Ok(result);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
                {
                    return Results.BadRequest(new { message = $"Nie udało się odczytać arkusza: {ex.Message}" });
                }
            })
            .DisableAntiforgery();

        // ---------- eksporty ----------
        group.MapGet("/export/excel", async (
            int? propertyId,
            int? roomId,
            int? categoryId,
            int? payerId,
            ShoppingStatus? status,
            ShoppingPriority? priority,
            string? search,
            string? sort,
            AppDbContext db,
            ExcelExportService excel,
            CancellationToken ct) =>
        {
            var data = await BuildShoppingReportAsync(
                db, propertyId, roomId, categoryId, payerId, status, priority, search, sort, ct);

            var bytes = excel.BuildShoppingList(data);

            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                BuildFileName("lista-zakupow", data.PropertyName, "xlsx"));
        });

        group.MapGet("/export/pdf", async (
            int? propertyId,
            int? roomId,
            int? categoryId,
            int? payerId,
            ShoppingStatus? status,
            ShoppingPriority? priority,
            string? search,
            string? sort,
            AppDbContext db,
            PdfExportService pdf,
            CancellationToken ct) =>
        {
            var data = await BuildShoppingReportAsync(
                db, propertyId, roomId, categoryId, payerId, status, priority, search, sort, ct);

            var bytes = pdf.BuildShoppingList(data);

            return Results.File(bytes, "application/pdf", BuildFileName("lista-zakupow", data.PropertyName, "pdf"));
        });
    }


    // ---------------------------------------------------------------- pomocnicze

    private static async Task<List<ShoppingItemDto>> LoadItemsAsync(
        AppDbContext db,
        int? propertyId,
        int? roomId,
        int? categoryId,
        int? payerId,
        ShoppingStatus? status,
        ShoppingPriority? priority,
        string? search,
        CancellationToken ct)
    {
        var query = db.ShoppingItems
            .Include(i => i.Room)
            .Include(i => i.ShoppingCategory)
            .Include(i => i.Payer)
            .Include(i => i.Invoice)
            .AsQueryable();

        if (propertyId.HasValue)
        {
            query = query.Where(i => i.PropertyId == propertyId);
        }

        if (roomId == ShoppingConstants.WholePropertyRoomId)
        {
            // „Całe mieszkanie” = pozycje bez przypisanego pomieszczenia.
            query = query.Where(i => i.RoomId == null);
        }
        else if (roomId.HasValue)
        {
            query = query.Where(i => i.RoomId == roomId);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(i => i.ShoppingCategoryId == categoryId);
        }

        if (payerId.HasValue)
        {
            query = query.Where(i => i.PayerId == payerId);
        }

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status);
        }

        if (priority.HasValue)
        {
            query = query.Where(i => i.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(i =>
                EF.Functions.ILike(i.Name, pattern) ||
                (i.Description != null && EF.Functions.ILike(i.Description, pattern)) ||
                (i.CalculationNotes != null && EF.Functions.ILike(i.CalculationNotes, pattern)));
        }

        var items = await query.AsNoTracking().ToListAsync(ct);
        return items.Select(i => i.ToDto()).ToList();
    }

    private static async Task<ShoppingItemDto?> LoadSingleAsync(AppDbContext db, int id, CancellationToken ct)
    {
        var item = await db.ShoppingItems
            .Include(i => i.Room)
            .Include(i => i.ShoppingCategory)
            .Include(i => i.Payer)
            .Include(i => i.Invoice)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        return item?.ToDto();
    }

    /// <summary>
    /// Sortowanie po nazwie kolumny; prefiks „-” oznacza malejąco.
    /// Domyślnie grupujemy po pomieszczeniach (to podstawowy widok listy remontowej).
    /// </summary>
    internal static List<ShoppingItemDto> SortItems(List<ShoppingItemDto> items, string? sort)
    {
        var descending = !string.IsNullOrEmpty(sort) && sort.StartsWith('-');
        var key = (descending ? sort![1..] : sort ?? string.Empty).Trim().ToLowerInvariant();

        IOrderedEnumerable<ShoppingItemDto> ordered = key switch
        {
            "name" or "pozycja" => Apply(items, i => i.Name, descending),
            "category" or "kategoria" => Apply(items, i => i.CategoryName ?? string.Empty, descending),
            "cost" or "koszt" => Apply(items, i => i.TotalCost ?? 0m, descending),
            "budget" or "budzet" => Apply(items, i => i.PlannedBudget ?? 0m, descending),
            "actual" => Apply(items, i => i.ActualCost ?? 0m, descending),
            "status" => Apply(items, i => (int)i.Status, descending),
            "priority" or "priorytet" => Apply(items, i => (int)i.Priority, descending),
            "ordinal" or "lp" => Apply(items, i => i.OrdinalNo, descending),
            _ => items
                // „Całe mieszkanie” na końcu — najpierw konkretne pomieszczenia.
                .OrderBy(i => i.RoomName == ShoppingConstants.WholePropertyRoomName ? 1 : 0)
                .ThenBy(i => i.RoomName, StringComparer.CurrentCulture)
                .ThenBy(i => i.OrdinalNo)
        };

        return ordered.ThenBy(i => i.Id).ToList();
    }

    private static IOrderedEnumerable<ShoppingItemDto> Apply<TKey>(
        List<ShoppingItemDto> items,
        Func<ShoppingItemDto, TKey> selector,
        bool descending) =>
        descending ? items.OrderByDescending(selector) : items.OrderBy(selector);

    private static async Task<int> NextOrdinalAsync(AppDbContext db, int propertyId, CancellationToken ct) =>
        (await db.ShoppingItems
            .Where(i => i.PropertyId == propertyId)
            .MaxAsync(i => (int?)i.OrdinalNo, ct) ?? 0) + 1;

    /// <summary>Sprawdza, czy wskazane pomieszczenie/kategoria/faktura naprawdę pasują do mieszkania.</summary>
    private static async Task<IResult?> ValidateReferencesAsync(
        AppDbContext db,
        ShoppingItemUpsertDto dto,
        CancellationToken ct)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == dto.PropertyId, ct))
        {
            return Results.BadRequest(new { message = "Wskazane mieszkanie nie istnieje." });
        }

        if (dto.RoomId.HasValue &&
            !await db.Rooms.AnyAsync(r => r.Id == dto.RoomId && r.PropertyId == dto.PropertyId, ct))
        {
            return Results.BadRequest(new { message = "Pomieszczenie nie należy do wybranego mieszkania." });
        }

        if (dto.ShoppingCategoryId.HasValue &&
            !await db.ShoppingCategories.AnyAsync(c => c.Id == dto.ShoppingCategoryId, ct))
        {
            return Results.BadRequest(new { message = "Wskazana kategoria nie istnieje." });
        }

        if (dto.InvoiceId.HasValue && !await db.Invoices.AnyAsync(i => i.Id == dto.InvoiceId, ct))
        {
            return Results.BadRequest(new { message = "Wskazana faktura nie istnieje." });
        }

        if (dto.PayerId.HasValue && !await db.Payers.AnyAsync(p => p.Id == dto.PayerId, ct))
        {
            return Results.BadRequest(new { message = "Wskazana osoba nie istnieje w słowniku." });
        }

        return null;
    }

    private static async Task<ShoppingReportData> BuildShoppingReportAsync(
        AppDbContext db,
        int? propertyId,
        int? roomId,
        int? categoryId,
        int? payerId,
        ShoppingStatus? status,
        ShoppingPriority? priority,
        string? search,
        string? sort,
        CancellationToken ct)
    {
        var items = SortItems(
            await LoadItemsAsync(db, propertyId, roomId, categoryId, payerId, status, priority, search, ct),
            sort);

        var propertyName = propertyId.HasValue
            ? await db.Properties
                  .Where(p => p.Id == propertyId)
                  .Select(p => p.Name)
                  .FirstOrDefaultAsync(ct) ?? "Wszystkie mieszkania"
            : "Wszystkie mieszkania";

        return new ShoppingReportData
        {
            PropertyName = propertyName,
            Items = items,
            Summary = ShoppingSummaryBuilder.Build(propertyId ?? 0, items)
        };
    }

    internal static string BuildFileName(string prefix, string propertyName, string extension)
    {
        var slug = new string(propertyName
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        return string.IsNullOrWhiteSpace(slug)
            ? $"{prefix}-{date}.{extension}"
            : $"{prefix}-{slug}-{date}.{extension}";
    }
}
