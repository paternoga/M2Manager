using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2Manager.Shared;

/// <summary>
/// Jedno źródło prawdy dla ustawień JSON-a. API i klient muszą serializować tak samo,
/// inaczej enumy przestają się zgadzać (string kontra liczba).
/// </summary>
public static class AppJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
