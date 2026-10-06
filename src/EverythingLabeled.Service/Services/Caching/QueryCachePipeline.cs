using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Caching;

public static class QueryCachePipeline
{
    public static async Task<T> GetOrLoadAsync<T>(
        ITagQueryCache cache,
        string key,
        int versionAtKey,
        Func<CancellationToken, Task<int>> readDataVersion,
        Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken) where T : class
    {
        var cached = await cache.TryGetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var loaded = await load(cancellationToken);
        var versionAfter = await readDataVersion(cancellationToken);
        if (versionAfter == versionAtKey)
            await cache.SetAsync(key, loaded, options: null, cancellationToken);

        return loaded;
    }
}
