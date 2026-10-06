using System.Text.Json;
using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class RedisTagQueryCache : ITagQueryCache
{
    private readonly CacheOptions _options;
    private readonly IRedisCacheDatabase _database;
    private readonly QueryCacheMetrics _metrics;
    private readonly ILogger<RedisTagQueryCache> _logger;

    internal RedisTagQueryCache(
        CacheOptions options,
        IRedisCacheDatabase database,
        QueryCacheMetrics metrics,
        ILogger<RedisTagQueryCache> logger)
    {
        _options = options;
        _database = database;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var physical = PhysicalKey(key);
            var bytes = await _database.StringGetAsync(physical, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                if (CacheVersionPrefix.TryGetLogicalPrefix(key, out var prefix) &&
                    CacheVersionPrefix.TryParse(prefix, out var version))
                {
                    await _database.SetRemoveAsync(IndexKey(version), key, cancellationToken)
                        .ConfigureAwait(false);
                }

                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(bytes, CacheJson.Options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Query cache Get failed key={Key}", key);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query cache Get failed key={Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, CacheJson.Options);
            if (bytes.Length > _options.MaxEntrySize)
            {
                _metrics.SkipMaxEntry();
                _logger.LogWarning(
                    "Query cache skip_max_entry key={Key} size={Size} max={Max}",
                    key,
                    bytes.Length,
                    _options.MaxEntrySize);
                return;
            }

            var physical = PhysicalKey(key);
            var ttl = options?.AbsoluteExpirationRelativeToNow;
            var indexKey = "";
            var member = "";
            if (CacheVersionPrefix.TryGetLogicalPrefix(key, out var prefix) &&
                CacheVersionPrefix.TryParse(prefix, out var version))
            {
                indexKey = IndexKey(version);
                member = key;
            }

            await _database.PipelineSetAndIndexAsync(
                    physical, bytes, ttl, indexKey, member, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query cache Set failed key={Key}", key);
        }
    }

    public async Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        if (!CacheVersionPrefix.TryParse(prefix, out var version))
        {
            _logger.LogWarning("Query cache InvalidatePrefix ignored prefix={Prefix}", prefix);
            return;
        }

        var indexKey = IndexKey(version);
        var members = await _database.SetMembersAsync(indexKey, cancellationToken).ConfigureAwait(false);
        var keys = new List<string>(members.Count + 1);
        foreach (var member in members)
            keys.Add(PhysicalKey(member));
        keys.Add(indexKey);
        await _database.KeyDeleteAsync(keys, cancellationToken).ConfigureAwait(false);
        _metrics.Invalidate();
    }

    private string PhysicalKey(string logicalKey) => _options.KeyPrefix + logicalKey;

    private string IndexKey(int version) => $"{_options.KeyPrefix}idx:{version}";
}
