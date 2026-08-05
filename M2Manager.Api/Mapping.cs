using System.Text.Json;
using M2Manager.Api.Data;
using M2Manager.Shared;
using M2Manager.Shared.Dtos;

namespace M2Manager.Api;

/// <summary>Ręczne mapowanie encji na DTO — bez AutoMappera, żeby wszystko było widoczne wprost.</summary>
public static class Mapping
{
    public static PropertyDto ToDto(this Property p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Address = p.Address,
        Purpose = p.Purpose,
        TotalAreaM2 = p.TotalAreaM2,
        DefaultRoomHeightM = p.DefaultRoomHeightM,
        RoomsCount = p.Rooms.Count
    };

    public static void ApplyTo(this PropertyUpsertDto dto, Property p)
    {
        p.Name = dto.Name.Trim();
        p.Address = Clean(dto.Address);
        p.Purpose = dto.Purpose;
        p.TotalAreaM2 = dto.TotalAreaM2;
        p.DefaultRoomHeightM = dto.DefaultRoomHeightM > 0 ? dto.DefaultRoomHeightM : 2.60m;
    }

    public static RoomDto ToDto(this Room r) => new()
    {
        Id = r.Id,
        PropertyId = r.PropertyId,
        Name = r.Name,
        SortOrder = r.SortOrder,
        FloorAreaM2 = r.FloorAreaM2,
        LengthM = r.LengthM,
        WidthM = r.WidthM,
        HeightM = r.HeightM,
        ManualWallAreaM2 = r.ManualWallAreaM2,
        ExcludedWallAreaM2 = r.ExcludedWallAreaM2,
        ManualCeilingAreaM2 = r.ManualCeilingAreaM2,
        IncludeInTotals = r.IncludeInTotals,
        Notes = r.Notes,
        GeometryJson = r.GeometryJson,
        Openings = r.Openings.OrderBy(o => o.Id).Select(o => o.ToDto()).ToList()
    };

    public static void ApplyTo(this RoomUpsertDto dto, Room r)
    {
        r.Name = dto.Name.Trim();
        r.SortOrder = dto.SortOrder;
        r.FloorAreaM2 = dto.FloorAreaM2;
        r.LengthM = dto.LengthM;
        r.WidthM = dto.WidthM;
        r.HeightM = dto.HeightM;
        r.ManualWallAreaM2 = dto.ManualWallAreaM2;
        r.ExcludedWallAreaM2 = dto.ExcludedWallAreaM2;
        r.ManualCeilingAreaM2 = dto.ManualCeilingAreaM2;
        r.IncludeInTotals = dto.IncludeInTotals;
        r.Notes = Clean(dto.Notes);
        r.GeometryJson = Clean(dto.GeometryJson);
    }

    public static RoomOpeningDto ToDto(this RoomOpening o) => new()
    {
        Id = o.Id,
        RoomId = o.RoomId,
        Type = o.Type,
        WidthCm = o.WidthCm,
        HeightCm = o.HeightCm,
        Count = o.Count,
        SubtractFromWalls = o.SubtractFromWalls,
        WallSide = o.WallSide,
        OffsetCm = o.OffsetCm,
        Notes = o.Notes
    };

    public static void ApplyTo(this RoomOpeningUpsertDto dto, RoomOpening o)
    {
        o.Type = dto.Type;
        o.WidthCm = dto.WidthCm;
        o.HeightCm = dto.HeightCm;
        o.Count = dto.Count > 0 ? dto.Count : 1;
        o.SubtractFromWalls = dto.SubtractFromWalls;
        o.WallSide = dto.WallSide;
        o.OffsetCm = dto.OffsetCm;
        o.Notes = Clean(dto.Notes);
    }

    public static InvoiceDto ToDto(this Invoice i, string? imageUrl = null) => new()
    {
        Id = i.Id,
        PropertyId = i.PropertyId,
        PropertyName = i.Property?.Name ?? string.Empty,
        RoomId = i.RoomId,
        RoomName = i.Room?.Name,
        ExpenseCategoryId = i.ExpenseCategoryId,
        ExpenseCategoryName = i.ExpenseCategory?.Name,
        Vendor = i.Vendor,
        Amount = i.Amount,
        Currency = i.Currency,
        IssueDate = i.IssueDate,
        Description = i.Description,
        ImageObjectKey = i.ImageObjectKey,
        ImageUrl = imageUrl,
        OcrStatus = i.OcrStatus,
        OcrRawResponse = i.OcrRawResponse,
        LineItems = ParseLineItems(i.OcrLineItemsJson),
        LinkedShoppingItemsCount = i.ShoppingItems.Count,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };

    /// <summary>Uszkodzony JSON pozycji nie może wywalić listy faktur — traktujemy go jak brak pozycji.</summary>
    public static List<OcrLineItemDto> ParseLineItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OcrLineItemDto>>(json, AppJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? SerializeLineItems(List<OcrLineItemDto> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items, AppJson.Options);

    public static ShoppingItemDto ToDto(this ShoppingItem s) => new()
    {
        Id = s.Id,
        OrdinalNo = s.OrdinalNo,
        PropertyId = s.PropertyId,
        RoomId = s.RoomId,
        RoomName = s.Room?.Name ?? ShoppingConstants.WholePropertyRoomName,
        ShoppingCategoryId = s.ShoppingCategoryId,
        CategoryName = s.ShoppingCategory?.Name,
        Name = s.Name,
        Description = s.Description,
        CalculationNotes = s.CalculationNotes,
        Quantity = s.Quantity,
        Unit = s.Unit,
        UnitCost = s.UnitCost,
        TotalCost = s.TotalCost,
        PlannedBudget = s.PlannedBudget,
        ActualCost = s.ActualCost,
        Vendor = s.Vendor,
        Link = s.Link,
        Status = s.Status,
        Priority = s.Priority,
        PurchaseDate = s.PurchaseDate,
        InvoiceId = s.InvoiceId,
        InvoiceLabel = BuildInvoiceLabel(s.Invoice),
        AssignedTo = s.AssignedTo,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    public static void ApplyTo(this ShoppingItemUpsertDto dto, ShoppingItem s)
    {
        s.PropertyId = dto.PropertyId;
        s.RoomId = dto.RoomId;
        s.ShoppingCategoryId = dto.ShoppingCategoryId;
        s.Name = dto.Name.Trim();
        s.Description = Clean(dto.Description);
        s.CalculationNotes = Clean(dto.CalculationNotes);
        s.Quantity = dto.Quantity;
        s.Unit = Clean(dto.Unit);
        s.UnitCost = dto.UnitCost;
        s.TotalCost = ResolveTotalCost(dto);
        s.PlannedBudget = dto.PlannedBudget;
        s.ActualCost = dto.ActualCost;
        s.Vendor = Clean(dto.Vendor);
        s.Link = Clean(dto.Link);
        s.Status = dto.Status;
        s.Priority = dto.Priority;
        s.PurchaseDate = dto.PurchaseDate;
        s.InvoiceId = dto.InvoiceId;
        s.AssignedTo = Clean(dto.AssignedTo);
    }

    /// <summary>Koszt całkowity: podany ręcznie wygrywa, w przeciwnym razie Ilość × Koszt szt.</summary>
    public static decimal? ResolveTotalCost(ShoppingItemUpsertDto dto)
    {
        if (dto.TotalCost.HasValue)
        {
            return dto.TotalCost;
        }

        if (dto.Quantity.HasValue && dto.UnitCost.HasValue)
        {
            return Math.Round(dto.Quantity.Value * dto.UnitCost.Value, 2, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    public static LookupDto ToDto(this ExpenseCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SortOrder = c.SortOrder
    };

    public static LookupDto ToDto(this ShoppingCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SortOrder = c.SortOrder
    };

    private static string? BuildInvoiceLabel(Invoice? invoice)
    {
        if (invoice is null)
        {
            return null;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(invoice.Vendor))
        {
            parts.Add(invoice.Vendor);
        }

        if (invoice.IssueDate.HasValue)
        {
            parts.Add(invoice.IssueDate.Value.ToString("yyyy-MM-dd"));
        }

        if (invoice.Amount.HasValue)
        {
            parts.Add($"{invoice.Amount.Value:0.00} {invoice.Currency}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : $"Faktura #{invoice.Id}";
    }

    /// <summary>Puste stringi zapisujemy jako null — łatwiej potem filtrować.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
