namespace EverythingLabeled.Service.Services.Caching;

public static class QueryCacheKeyFactory
{
    public static string ForQuery(
        int dataVersion,
        string op,
        string normalizedQuery,
        string expand,
        int maxDepth,
        int expandedTagLimit,
        int limit,
        int offset,
        string kind = "all") =>
        $"v{dataVersion}:{op}:{normalizedQuery}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}:{kind}";

    public static string ForSuggest(int dataVersion, string normalizedPrefix, int limit) =>
        $"v{dataVersion}:suggest:{normalizedPrefix}:{limit}";
}
