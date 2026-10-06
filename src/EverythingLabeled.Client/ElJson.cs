using System.Text.Json;
using System.Text.Json.Serialization;

namespace EverythingLabeled.Client;

public static class ElJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
