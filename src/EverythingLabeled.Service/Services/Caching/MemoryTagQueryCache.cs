using System.Collections.Concurrent;
using EverythingLabeled.Service.Services.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class MemoryTagQueryCache : ITagQueryCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly CacheOptions _options;
    private readonly QueryCacheMetrics _metrics;
    private readonly ILogger<MemoryTagQueryCache> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _index = new();

    public MemoryTagQueryCache(
        CacheOptions options,
        QueryCacheMetrics metrics,
        ILogger<MemoryTagQueryCache> logger)
    {
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
    }

    public Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            if (_cache.TryGetValue(key, out T? value))
                return Task.FromResult(value);
            return Task.FromResult<T?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query cache Get failed key={Key}", key);
            return Task.FromResult<T?>(null);
        }
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var size = CacheEntrySizer.Estimate(key, value);
            if (size > _options.MaxEntrySize || size > int.MaxValue)
            {
                _metrics.SkipMaxEntry();
                _logger.LogWarning(
                    "Query cache skip_max_entry key={Key} size={Size} max={Max}",
                    key,
                    size,
                    _options.MaxEntrySize);
                return Task.CompletedTask;
            }

            var entryOptions = new MemoryCacheEntryOptions
            {
                Size = (int)size,
                AbsoluteExpirationRelativeToNow = options?.AbsoluteExpirationRelativeToNow
            };
            entryOptions.RegisterPostEvictionCallback(OnEvicted);

            if (CacheVersionPrefix.TryGetLogicalPrefix(key, out var prefix))
            {
                var inner = _index.GetOrAdd(prefix, static _ => new ConcurrentDictionary<string, byte>());
                inner[key] = 0;
            }

            _cache.Set(key, value, entryOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query cache Set failed key={Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        if (!CacheVersionPrefix.TryParse(prefix, out _))
        {
            _logger.LogWarning("Query cache InvalidatePrefix ignored prefix={Prefix}", prefix);
            return Task.CompletedTask;
        }

        if (_index.TryRemove(prefix, out var inner))
        {
            foreach (var key in inner.Keys)
                _cache.Remove(key);
        }

        _metrics.Invalidate();
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();

    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (reason == EvictionReason.Replaced)
            return;
        if (key is not string logicalKey)
            return;
        if (!CacheVersionPrefix.TryGetLogicalPrefix(logicalKey, out var prefix))
            return;
        if (!_index.TryGetValue(prefix, out var inner))
            return;

        inner.TryRemove(logicalKey, out _);
        if (inner.IsEmpty)
            _index.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(prefix, inner));
    }
}
