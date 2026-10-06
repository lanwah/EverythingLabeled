using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class QueryCachePipeline(
    ITagQueryCache cache,
    QueryCacheSingleflight singleflight,
    QueryCacheMetrics metrics,
    ILogger<QueryCachePipeline> logger)
{
    public async Task<T> GetOrLoadAsync<T>(
        string key,
        int versionAtKey,
        Func<CancellationToken, Task<int>> readDataVersion,
        Func<CancellationToken, Task<T>> load,
        CacheEntryOptions? options,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            var cached = await cache.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                metrics.Hit();
                return cached;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Query cache Get failed key={Key}", key);
        }

        metrics.Miss();

        return await singleflight.RunAsync(key, async () =>
        {
            var loaded = await load(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var versionAfter = await readDataVersion(CancellationToken.None).ConfigureAwait(false);
                if (versionAfter == versionAtKey)
                {
                    await cache.SetAsync(key, Snapshot(loaded), options, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Query cache Set failed key={Key}", key);
            }

            return loaded;
        }).ConfigureAwait(false);
    }

    private static T Snapshot<T>(T loaded) where T : class
    {
        if (loaded is Paged<QueryHitDto> hits)
        {
            return (T)(object)new Paged<QueryHitDto>
            {
                Items = hits.Items.ToArray(),
                Offset = hits.Offset,
                Limit = hits.Limit,
                Total = hits.Total,
            };
        }

        if (loaded is Paged<SuggestItemDto> suggest)
        {
            return (T)(object)new Paged<SuggestItemDto>
            {
                Items = suggest.Items.ToArray(),
                Offset = suggest.Offset,
                Limit = suggest.Limit,
                Total = suggest.Total,
            };
        }

        return loaded;
    }
}
