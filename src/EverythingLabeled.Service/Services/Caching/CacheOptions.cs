namespace EverythingLabeled.Service.Services.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";
    public const long DefaultSizeLimit = 134217728;
    public const long DefaultMaxEntrySize = 2097152;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultRedisConnectTimeout = TimeSpan.FromSeconds(5);
    public const string DefaultKeyPrefix = "el:qc:";

    public string Provider { get; set; } = "Memory";
    public long SizeLimit { get; set; } = DefaultSizeLimit;
    public long MaxEntrySize { get; set; } = DefaultMaxEntrySize;
    public TimeSpan SuggestTtl { get; set; } = DefaultTtl;
    public TimeSpan PreviewTtl { get; set; } = DefaultTtl;
    public string? RedisConnection { get; set; }
    public TimeSpan RedisConnectTimeout { get; set; } = DefaultRedisConnectTimeout;
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;

    public string NormalizedProvider() => Provider.Trim();

    public bool IsMemory() => NormalizedProvider().Equals("Memory", StringComparison.OrdinalIgnoreCase);
    public bool IsRedis() => NormalizedProvider().Equals("Redis", StringComparison.OrdinalIgnoreCase);
    public bool IsNull() => NormalizedProvider().Equals("Null", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!IsMemory() && !IsRedis() && !IsNull())
            throw new InvalidOperationException($"Cache:Provider '{Provider}' is not Memory, Redis, or Null.");
        if (SizeLimit <= 0)
            throw new InvalidOperationException("Cache:SizeLimit must be > 0.");
        if (MaxEntrySize <= 0 || MaxEntrySize > SizeLimit)
            throw new InvalidOperationException("Cache:MaxEntrySize must be > 0 and <= SizeLimit.");
        if (SuggestTtl <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:SuggestTtl must be > 0.");
        if (PreviewTtl <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:PreviewTtl must be > 0.");
        if (!IsRedis())
            return;
        if (string.IsNullOrWhiteSpace(RedisConnection))
            throw new InvalidOperationException("Cache:RedisConnection is required when Provider is Redis.");
        if (RedisConnectTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:RedisConnectTimeout must be > 0.");
        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new InvalidOperationException("Cache:KeyPrefix must be non-empty when Provider is Redis.");
    }
}
