using System.Text.Json;
using System.Text.Json.Serialization;

namespace PvPSentinel.FrontlineCore.Diagnostics;

internal static class FrontlineJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
