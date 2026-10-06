using System.Text.Json;

namespace EverythingLabeled.Service.Services.Caching;

internal static class CacheJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
