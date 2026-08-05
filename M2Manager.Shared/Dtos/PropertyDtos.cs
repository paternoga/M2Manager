using System.ComponentModel.DataAnnotations;
using M2Manager.Shared.Areas;

namespace M2Manager.Shared.Dtos;

public sealed class PropertyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public PropertyPurpose Purpose { get; set; }
    public decimal? TotalAreaM2 { get; set; }
    public decimal DefaultRoomHeightM { get; set; } = AreaCalculator.FallbackRoomHeightM;
    public int RoomsCount { get; set; }
}

public sealed class PropertyUpsertDto
{
    [Required(ErrorMessage = "Nazwa jest wymagana.")]
    [StringLength(200, ErrorMessage = "Nazwa może mieć maksymalnie 200 znaków.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Address { get; set; }

    public PropertyPurpose Purpose { get; set; } = PropertyPurpose.OwnOccupied;

    [Range(0, 100000, ErrorMessage = "Powierzchnia musi być dodatnia.")]
    public decimal? TotalAreaM2 { get; set; }

    [Range(1.0, 10.0, ErrorMessage = "Wysokość musi mieścić się w zakresie 1–10 m.")]
    public decimal DefaultRoomHeightM { get; set; } = AreaCalculator.FallbackRoomHeightM;
}

public sealed class RoomOpeningDto : IOpeningAreaSource
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public OpeningType Type { get; set; } = OpeningType.Window;
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public int Count { get; set; } = 1;
    public bool SubtractFromWalls { get; set; } = true;
    public WallSide? WallSide { get; set; }
    public decimal? OffsetCm { get; set; }
    public string? Notes { get; set; }
}

public sealed class RoomOpeningUpsertDto
{
    public OpeningType Type { get; set; } = OpeningType.Window;

    [Range(1, 1000, ErrorMessage = "Szerokość musi mieścić się w zakresie 1–1000 cm.")]
    public decimal WidthCm { get; set; }

    [Range(1, 1000, ErrorMessage = "Wysokość musi mieścić się w zakresie 1–1000 cm.")]
    public decimal HeightCm { get; set; }

    [Range(1, 100, ErrorMessage = "Liczba sztuk musi mieścić się w zakresie 1–100.")]
    public int Count { get; set; } = 1;

    public bool SubtractFromWalls { get; set; } = true;
    public WallSide? WallSide { get; set; }
    public decimal? OffsetCm { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public sealed class RoomDto : IRoomAreaSource
{
    public int Id { get; set; }
    public int PropertyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public decimal? FloorAreaM2 { get; set; }
    public decimal? LengthM { get; set; }
    public decimal? WidthM { get; set; }
    public decimal? HeightM { get; set; }
    public decimal? ManualWallAreaM2 { get; set; }
    public decimal? ExcludedWallAreaM2 { get; set; }
    public decimal? ManualCeilingAreaM2 { get; set; }
    public bool IncludeInTotals { get; set; } = true;
    public string? Notes { get; set; }
    public string? GeometryJson { get; set; }
    public List<RoomOpeningDto> Openings { get; set; } = [];
}

public sealed class RoomUpsertDto
{
    [Required(ErrorMessage = "Nazwa pomieszczenia jest wymagana.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    [Range(0, 10000)]
    public decimal? FloorAreaM2 { get; set; }

    [Range(0, 1000)]
    public decimal? LengthM { get; set; }

    [Range(0, 1000)]
    public decimal? WidthM { get; set; }

    [Range(0, 10)]
    public decimal? HeightM { get; set; }

    [Range(0, 10000)]
    public decimal? ManualWallAreaM2 { get; set; }

    [Range(0, 10000)]
    public decimal? ExcludedWallAreaM2 { get; set; }

    [Range(0, 10000)]
    public decimal? ManualCeilingAreaM2 { get; set; }

    public bool IncludeInTotals { get; set; } = true;

    [StringLength(2000)]
    public string? Notes { get; set; }

    public string? GeometryJson { get; set; }
}

/// <summary>Wyliczenia dla jednego pomieszczenia zwracane przez /api/properties/{id}/areas.</summary>
public sealed class RoomAreaDto
{
    public int RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public bool IncludeInTotals { get; set; }
    public RoomAreaResult Area { get; set; } = new();
}

public sealed class PropertyAreasDto
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public decimal DefaultRoomHeightM { get; set; }
    public List<RoomAreaDto> Rooms { get; set; } = [];
    public PropertyAreaSummary Summary { get; set; } = new();
}

/// <summary>Jedna pozycja w batchowym zapisie rzutu.</summary>
public sealed class RoomLayoutDto
{
    public int RoomId { get; set; }
    public string? GeometryJson { get; set; }

    /// <summary>Opcjonalnie: edytor po przeciągnięciu odświeża też wymiary liczbowe.</summary>
    public decimal? LengthM { get; set; }

    public decimal? WidthM { get; set; }
    public decimal? FloorAreaM2 { get; set; }
}

public sealed class LayoutSaveRequest
{
    public List<RoomLayoutDto> Rooms { get; set; } = [];
}
