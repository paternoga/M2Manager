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
                var expenseCategories = await db.ExpenseCategories
                    .OrderBy(c => c.SortOrder)
                    .Select(c => c.Name)
                    .ToListAsync(ct);

                var shoppingCategories = await db.ShoppingCategories
                    .OrderBy(c => c.SortOrder)
                    .Select(c => c.Name)
                    .ToListAsync(ct);

                var extraction = await ocr.ExtractAsync(
                    bytes, contentType, new OcrCategories(expenseCategories, shoppingCategories), ct);

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
                    invoice.OcrLineItemsJson = Mapping.SerializeLineItems(extraction.LineItems);
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
                await db.Entry(invoice).Reference(i => i.Payer).LoadAsync(ct);
                await db.Entry(invoice).Collection(i => i.ShoppingItems).LoadAsync(ct);

                var url = await storage.GetViewUrlAsync(objectKey, ct);
                return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToDto(url));
            })
            .DisableAntiforgery(); // cookie ma SameSite=Lax, więc żądania cross-site i tak nie niosą sesji

        // ---------- lista z filtrami ----------
        group.MapGet("/", async (
            int? propertyId,
            int? roomId,
            int? categoryId,
            int? payerId,
            DateOnly? from,
            DateOnly? to,
            int? page,
            int? pageSize,
            AppDbContext db,
            IObjectStorage storage,
            CancellationToken ct) =>
        {
            var query = BuildFilteredQuery(db, propertyId, roomId, categoryId, from, to, payerId);

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
                .Include(i => i.Payer)
                .Include(i => i.ShoppingItems)
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
                .Include(i => i.Payer)
                .Include(i => i.ShoppingItems)
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

            if (dto.PayerId.HasValue && !await db.Payers.AnyAsync(p => p.Id == dto.PayerId, ct))
            {
                return Results.BadRequest(new { message = "Wskazana osoba nie istnieje w słowniku." });
            }

            invoice.PropertyId = dto.PropertyId;
            invoice.RoomId = dto.RoomId;
            invoice.ExpenseCategoryId = dto.ExpenseCategoryId;
            invoice.PayerId = dto.PayerId;
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

        // ---------- przeniesienie pozycji faktury na listę zakupów ----------
        group.MapPost("/{id:int}/shopping-items", async (
            int id,
            CreateShoppingItemsFromInvoiceRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null)
            {
                return Results.NotFound();
            }

            var selected = request.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .ToList();

            if (selected.Count == 0)
            {
                return Results.BadRequest(new { message = "Nie wybrano żadnej pozycji." });
            }

            // Pomieszczenie z żądania ma pierwszeństwo, ale musi należeć do mieszkania z faktury.
            var roomId = request.RoomId ?? invoice.RoomId;
            if (roomId.HasValue &&
                !await db.Rooms.AnyAsync(r => r.Id == roomId && r.PropertyId == invoice.PropertyId, ct))
            {
                return Results.BadRequest(new { message = "Pomieszczenie nie należy do mieszkania z faktury." });
            }

            var categories = await db.ShoppingCategories
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);

            var ordinal = (await db.ShoppingItems
                .Where(i => i.PropertyId == invoice.PropertyId)
                .MaxAsync(i => (int?)i.OrdinalNo, ct) ?? 0);

            var created = new List<ShoppingItem>();

            foreach (var line in selected)
            {
                var normalized = TextNormalizer.Normalize(line.SuggestedCategoryName);

                var categoryId = normalized.Length == 0
                    ? null
                    : categories
                        .Where(c => TextNormalizer.Normalize(c.Name) == normalized)
                        .Select(c => (int?)c.Id)
                        .FirstOrDefault();

                var total = line.TotalPrice
                            ?? (line.Quantity.HasValue && line.UnitPrice.HasValue
                                ? Math.Round(line.Quantity.Value * line.UnitPrice.Value, 2, MidpointRounding.AwayFromZero)
                                : null);

                var item = new ShoppingItem
                {
                    OrdinalNo = ++ordinal,
                    PropertyId = invoice.PropertyId,
                    RoomId = roomId,
                    ShoppingCategoryId = categoryId,

                    // Kto zapłacił fakturę, ten finansuje wszystkie jej pozycje.
                    PayerId = invoice.PayerId,
                    Name = line.Name.Trim(),
                    Quantity = line.Quantity,
                    Unit = string.IsNullOrWhiteSpace(line.Unit) ? null : line.Unit.Trim(),
                    UnitCost = line.UnitPrice,
                    TotalCost = total,

                    // Pozycja pochodzi z faktury, więc to już wydana kwota, nie szacunek.
                    ActualCost = total,
                    Status = request.Status,
                    Priority = ShoppingPriority.MustHave,
                    PurchaseDate = invoice.IssueDate,
                    InvoiceId = invoice.Id,
                    Vendor = invoice.Vendor,
                    CalculationNotes = $"Z faktury #{invoice.Id}"
                };

                db.ShoppingItems.Add(item);
                created.Add(item);
            }

            await db.SaveChangesAsync(ct);

            var ids = created.Select(i => i.Id).ToList();

            var result = await db.ShoppingItems
                .Where(i => ids.Contains(i.Id))
                .Include(i => i.Room)
                .Include(i => i.ShoppingCategory)
                .Include(i => i.Invoice)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(result.Select(i => i.ToDto()).ToList());
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
        DateOnly? to,
        int? payerId = null)
    {
        var query = db.Invoices
            .Include(i => i.Property)
            .Include(i => i.Room)
            .Include(i => i.ExpenseCategory)
            .Include(i => i.Payer)
            .Include(i => i.ShoppingItems)
            .AsQueryable();

        if (propertyId.HasValue)
        {
            query = query.Where(i => i.PropertyId == propertyId);
        }

        if (roomId == ShoppingConstants.WholePropertyRoomId)
        {
            // „Całe mieszkanie” = faktury nieprzypisane do konkretnego pomieszczenia.
            query = query.Where(i => i.RoomId == null);
        }
        else if (roomId.HasValue)
        {
            query = query.Where(i => i.RoomId == roomId);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(i => i.ExpenseCategoryId == categoryId);
        }

        if (payerId.HasValue)
        {
            query = query.Where(i => i.PayerId == payerId);
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
