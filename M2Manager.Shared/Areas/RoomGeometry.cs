using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2Manager.Shared.Areas;

/// <summary>Punkt na rzucie, w centymetrach.</summary>
public sealed class GeometryPoint
{
    public decimal X { get; set; }
    public decimal Y { get; set; }

    public GeometryPoint() { }

    public GeometryPoint(decimal x, decimal y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Kształt i położenie pomieszczenia na rzucie. Wszystkie wartości w CENTYMETRACH,
/// żeby uniknąć błędów zaokrągleń przy przyciąganiu do siatki co 10 cm.
/// Domyślnie prostokąt (X, Y, WidthCm, HeightCm); opcjonalnie wielokąt (<see cref="Points"/>),
/// który ma pierwszeństwo, jeśli zawiera co najmniej 3 punkty.
/// </summary>
public sealed class RoomGeometry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    /// <summary>Opcjonalny wielokąt (np. pomieszczenie w kształcie „L”), w cm.</summary>
    public List<GeometryPoint>? Points { get; set; }

    [JsonIgnore]
    public bool IsPolygon => Points is { Count: >= 3 };

    /// <summary>Bezpieczne parsowanie — uszkodzony JSON traktujemy jak brak geometrii.</summary>
    public static RoomGeometry? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RoomGeometry>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Obwód w metrach albo null, gdy geometria jest pusta/zdegenerowana.</summary>
    public decimal? PerimeterM()
    {
        if (IsPolygon)
        {
            var pts = Points!;
            decimal sumCm = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                sumCm += SegmentLengthCm(a, b);
            }

            return sumCm > 0 ? sumCm / 100m : null;
        }

        if (WidthCm > 0 && HeightCm > 0)
        {
            return 2m * (WidthCm + HeightCm) / 100m;
        }

        return null;
    }

    /// <summary>Powierzchnia w m² albo null, gdy geometria jest pusta/zdegenerowana.</summary>
    public decimal? AreaM2()
    {
        if (IsPolygon)
        {
            // Wzór na powierzchnię wielokąta (shoelace).
            var pts = Points!;
            decimal twiceArea = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                twiceArea += (a.X * b.Y) - (b.X * a.Y);
            }

            var areaCm2 = Math.Abs(twiceArea) / 2m;
            return areaCm2 > 0 ? areaCm2 / 10_000m : null;
        }

        if (WidthCm > 0 && HeightCm > 0)
        {
            return WidthCm * HeightCm / 10_000m;
        }

        return null;
    }

    private static decimal SegmentLengthCm(GeometryPoint a, GeometryPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        // Odcinki pionowe/poziome (a takie rysuje edytor) liczymy bez straty precyzji.
        if (dx == 0)
        {
            return Math.Abs(dy);
        }

        if (dy == 0)
        {
            return Math.Abs(dx);
        }

        return (decimal)Math.Sqrt((double)((dx * dx) + (dy * dy)));
    }
}
