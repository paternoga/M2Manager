using M2Manager.Api.Configuration;
using M2Manager.Api.Data;
using M2Manager.Api.Services;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace M2Manager.Api.Endpoints;

/// <summary>Moduł 1: faktury i paragony ze zdjęciem oraz słownik kategorii wydatków.</summary>
public static class InvoiceEndpoints
{
    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        MapInvoices(app);
        MapExpenseCategories(app);
        MapLocalFiles(app);
    }

    private static void MapInvoices(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices").RequireAuthorization();

        // ---------- upload zdjęcia + odczyt AI ----------
        group.MapPost("/upload", async (
                HttpRequest http,
                AppDbContext db,
                IObjectStorage storage,
                IOcrService ocr,
                IOptions<UploadOptions> uploadOptions,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("InvoiceUpload");
                var options = uploadOptions.Value;

                if (!http.HasFormContentType)
                {
                    return Results.BadRequest(new { message = "Oczekiwano formularza multipart ze zdjęciem." });
                }

                var form = await http.ReadFormAsync(ct);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { message = "Nie przesłano pliku." });
                }

                if (file.Length > options.MaxFileSizeBytes)
                {
                    return Results.BadRequest(new
                    {
                        message = $"Plik jest za duży (maksymalnie {options.MaxFileSizeMb} MB)."
                    });
                }

                var contentType = (file.ContentType ?? "application/octet-stream").ToLowerInvariant();
                if (!options.AllowedContentTypes.Contains(contentType))
                {
                    return Results.BadRequest(new
                    {
                        message = $"Nieobsługiwany format pliku: {contentType}."
                    });
                }

                // Mieszkanie: z formularza albo pierwsze z bazy (upload z telefonu ma być jednym kliknięciem).
                var propertyId = ParseInt(form["propertyId"]);
                var roomId = ParseInt(form["roomId"]);

                propertyId ??= await db.Properties.OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
                if (propertyId is null)
                {
                    return Results.BadRequest(new { message = "Najpierw dodaj mieszkanie." });
                }

                if (roomId.HasValue && !await db.Rooms.AnyAsync(r => r.Id == roomId && r.PropertyId == propertyId, ct))
                {
                    roomId = null;
                }

                // Bajty trzymamy w pamięci — potrzebne i do R2, i do zapytania do modelu AI.
                byte[] bytes;
                await using (var input = file.OpenReadStream())
                await using (var buffer = new MemoryStream())
                {
                    await input.CopyToAsync(buffer, ct);
                    bytes = buffer.ToArray();
                }

                var objectKey = IObjectStorage.BuildObjectKey(file.FileName, DateTime.UtcNow);

                await using (var upload = new MemoryStream(bytes, writable: false))
                {
                    await storage.UploadAsync(upload, objectKey, contentType, ct);
                }

                // Odczyt AI. Cokolwiek się stanie, zdjęcie już jest zapisane.
                var categories = await db.ExpenseCategories
                    .OrderBy(c => c.SortOrder)
                    .Select(c => c.Name)
                    .ToListAsync(ct);

                var extraction = await ocr.ExtractAsync(bytes, contentType, categories, ct);

                var invoice = new Invoice
                {
                    PropertyId = propertyId.Value,
                    RoomId = roomId,
                    ImageObjectKey = objectKey,
                    Currency = "PLN",
                    OcrStatus = extraction.Success ? OcrStatus.Extracted : OcrStatus.Failed,
                    OcrRawResponse = Truncate(extraction.RawResponse ?? extraction.Error, 8000)
                };

                if (extraction.Success)
                {
                    invoice.Vendor = extraction.Vendor;
                    invoice.Amount = extraction.Amount;
                    invoice.Currency = string.IsNullOrWhiteSpace(extraction.Currency) ? "PLN" : extraction.Currency;
                    invoice.IssueDate = extraction.IssueDate;
                    invoice.ExpenseCategoryId = await MatchCategoryIdAsync(db, extraction.SuggestedCategoryName, ct);
                }
                else
                {
                    logger.LogInformation("Odczyt AI nieudany: {Error}", extraction.Error);
                }

                db.Invoices.Add(invoice);
                await db.SaveChangesAsync(ct);

                await db.Entry(invoice).Reference(i => i.Property).LoadAsync(ct);
                await db.Entry(invoice).Reference(i => i.Room).LoadAsync(ct);
                await db.Entry(invoice).Reference(i => i.ExpenseCategory).LoadAsync(ct);

                var url = await storage.GetViewUrlAsync(objectKey, ct);
                return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToDto(url));
            })
            .DisableAntiforgery(); // cookie ma SameSite=Lax, więc żądania cross-site i tak nie niosą sesji

        // ---------- lista z filtrami ----------
        group.MapGet("/", async (
            int? propertyId,
            int? roomId,
            int? categoryId,
            DateOnly? from,
            DateOnly? to,
            int? page,
            int? pageSize,
            AppDbContext db,
            IObjectStorage storage,
            CancellationToken ct) =>
        {
            var query = BuildFilteredQuery(db, propertyId, roomId, categoryId, from, to);

            var totalCount = await query.CountAsync(ct);

            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 25, 1, 200);

            var items = await query
                .OrderByDescending(i => i.IssueDate ?? DateOnly.FromDateTime(i.CreatedAt))
                .ThenByDescending(i => i.Id)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .AsNoTracking()
                .ToListAsync(ct);

            var dtos = new List<InvoiceDto>(items.Count);
            foreach (var invoice in items)
            {
                dtos.Add(invoice.ToDto(await storage.GetViewUrlAsync(invoice.ImageObjectKey, ct)));
            }

            return Results.Ok(new PagedResult<InvoiceDto>
            {
                Items = dtos,
                Page = currentPage,
                PageSize = size,
                TotalCount = totalCount
            });
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db, IObjectStorage storage, CancellationToken ct) =>
        {
            var invoice = await db.Invoices
                .Include(i => i.Property)
                .Include(i => i.Room)
                .Include(i => i.ExpenseCategory)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, ct);

            if (invoice is null)
            {
                return Results.NotFound();
            }

            var url = await storage.GetViewUrlAsync(invoice.ImageObjectKey, ct);
            return Results.Ok(invoice.ToDto(url));
        });

        // ---------- ręczna korekta danych z AI ----------
        group.MapPut("/{id:int}", async (
            int id,
            InvoiceUpsertDto dto,
            AppDbContext db,
            IObjectStorage storage,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidationProblemOrNull(dto) is { } problem)
            {
                return problem;
            }

            var invoice = await db.Invoices
                .Include(i => i.Property)
                .Include(i => i.Room)
                .Include(i => i.ExpenseCategory)
                .FirstOrDefaultAsync(i => i.Id == id, ct);

            if (invoice is null)
            {
                return Results.NotFound();
            }

            if (!await db.Properties.AnyAsync(p => p.Id == dto.PropertyId, ct))
            {
                return Results.BadRequest(new { message = "Wskazane mieszkanie nie istnieje." });
            }

            if (dto.RoomId.HasValue &&
                !await db.Rooms.AnyAsync(r => r.Id == dto.RoomId && r.PropertyId == dto.PropertyId, ct))
            {
                return Results.BadRequest(new { message = "Pomieszczenie nie należy do wybranego mieszkania." });
            }

            invoice.PropertyId = dto.PropertyId;
            invoice.RoomId = dto.RoomId;
            invoice.ExpenseCategoryId = dto.ExpenseCategoryId;
            invoice.Vendor = string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor.Trim();
            invoice.Amount = dto.Amount;
            invoice.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "PLN" : dto.Currency.Trim().ToUpperInvariant();
            invoice.IssueDate = dto.IssueDate;
            invoice.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

            // Zapis formularza to potwierdzenie przez człowieka.
            if (dto.MarkConfirmed)
            {
                invoice.OcrStatus = OcrStatus.Confirmed;
            }

            await db.SaveChangesAsync(ct);

            await db.Entry(invoice).Reference(i => i.Property).LoadAsync(ct);
            await db.Entry(invoice).Reference(i => i.Room).LoadAsync(ct);
            await db.Entry(invoice).Reference(i => i.ExpenseCategory).LoadAsync(ct);

            var url = await storage.GetViewUrlAsync(invoice.ImageObjectKey, ct);
            return Results.Ok(invoice.ToDto(url));
        });

        group.MapDelete("/{id:int}", async (
            int id,
            AppDbContext db,
            IObjectStorage storage,
            CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null)
            {
                return Results.NotFound();
            }

            // Najpierw plik, potem rekord — osierocony plik w R2 jest mniej szkodliwy niż rekord bez zdjęcia.
            await storage.DeleteAsync(invoice.ImageObjectKey, ct);

            db.Invoices.Remove(invoice);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static void MapExpenseCategories(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expense-categories").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var categories = await db.ExpenseCategories
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(categories.Select(c => c.ToDto()).ToList());
        });

        group.MapPost("/", async (LookupUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { message = "Nazwa kategorii jest wymagana." });
            }

            if (await db.ExpenseCategories.AnyAsync(c => c.Name == name, ct))
            {
                return Results.Conflict(new { message = "Kategoria o tej nazwie już istnieje." });
            }

            var maxOrder = await db.ExpenseCategories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0;

            var category = new ExpenseCategory
            {
                Name = name,
                SortOrder = dto.SortOrder > 0 ? dto.SortOrder : maxOrder + 10
            };

            db.ExpenseCategories.Add(category);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/expense-categories/{category.Id}", category.ToDto());
        });

        group.MapPut("/{id:int}", async (int id, LookupUpsertDto dto, AppDbContext db, CancellationToken ct) =>
        {
            var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null)
            {
                return Results.NotFound();
            }

            var name = dto.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { message = "Nazwa kategorii jest wymagana." });
            }

            category.Name = name;
            category.SortOrder = dto.SortOrder;
            await db.SaveChangesAsync(ct);

            return Results.Ok(category.ToDto());
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category is null)
            {
                return Results.NotFound();
            }

            // FK ma OnDelete=SetNull — faktury zostają, tracą tylko kategorię.
            db.ExpenseCategories.Remove(category);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    /// <summary>Serwowanie zdjęć w trybie lokalnym (bez R2). Przy R2 klient dostaje presigned URL i tu nie trafia.</summary>
    private static void MapLocalFiles(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/files/{**objectKey}", async (
                string objectKey,
                IObjectStorage storage,
                CancellationToken ct) =>
            {
                var stream = await storage.OpenReadAsync(Uri.UnescapeDataString(objectKey), ct);
                if (stream is null)
                {
                    return Results.NotFound();
                }

                var contentType = GuessContentType(objectKey);
                return Results.Stream(stream, contentType);
            })
            .RequireAuthorization();
    }

    // ---------------------------------------------------------------- pomocnicze

    internal static IQueryable<Invoice> BuildFilteredQuery(
        AppDbContext db,
        int? propertyId,
        int? roomId,
        int? categoryId,
        DateOnly? from,
        DateOnly? to)
    {
        var query = db.Invoices
            .Include(i => i.Property)
            .Include(i => i.Room)
            .Include(i => i.ExpenseCategory)
            .AsQueryable();

        if (propertyId.HasValue)
        {
            query = query.Where(i => i.PropertyId == propertyId);
        }

        if (roomId.HasValue)
        {
            query = query.Where(i => i.RoomId == roomId);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(i => i.ExpenseCategoryId == categoryId);
        }

        if (from.HasValue)
        {
            query = query.Where(i => i.IssueDate != null && i.IssueDate >= from);
        }

        if (to.HasValue)
        {
            query = query.Where(i => i.IssueDate != null && i.IssueDate <= to);
        }

        return query;
    }

    /// <summary>
    /// Dopasowanie kategorii podpowiedzianej przez AI. Porównujemy po normalizacji, bo modele
    /// bywają niekonsekwentne z polskimi znakami — potrafią zwrócić „Wyposazenie” zamiast „Wyposażenie”.
    /// </summary>
    private static async Task<int?> MatchCategoryIdAsync(AppDbContext db, string? suggested, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(suggested))
        {
            return null;
        }

        var normalized = TextNormalizer.Normalize(suggested);
        if (normalized.Length == 0)
        {
            return null;
        }

        var categories = await db.ExpenseCategories
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        return categories
            .Where(c => TextNormalizer.Normalize(c.Name) == normalized)
            .Select(c => (int?)c.Id)
            .FirstOrDefault();
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static string GuessContentType(string objectKey) =>
        Path.GetExtension(objectKey).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".pdf" => "application/pdf",
            _ => "image/jpeg"
        };
}
