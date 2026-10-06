using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace EverythingLabeled.Service.Tests;

public sealed class RedisTagQueryCacheIntegrationTests
{
    private static bool TryConnect(out ConnectionMultiplexer? mux)
    {
        mux = null;
        var url = Environment.GetEnvironmentVariable("REDIS_TEST_URL");
        var cs = string.IsNullOrWhiteSpace(url) ? "127.0.0.1:6379" : url;
        try
        {
            var cfg = ConfigurationOptions.Parse(cs);
            cfg.AbortOnConnectFail = true;
            cfg.ConnectTimeout = 500;
            mux = ConnectionMultiplexer.Connect(cfg);
            mux.GetDatabase().Ping();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task Roundtrip_and_prefix_invalidate()
    {
        if (!TryConnect(out var mux) || mux is null)
            return;
        try
        {
            var options = new CacheOptions
            {
                Provider = "Redis",
                RedisConnection = mux.Configuration,
                KeyPrefix = $"el:qc:test:{Guid.NewGuid():N}:",
                RedisConnectTimeout = TimeSpan.FromSeconds(5),
            };
            options.Validate();
            var cache = new RedisTagQueryCache(
                options,
                new StackExchangeRedisDatabase(mux.GetDatabase()),
                new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
                NullLogger<RedisTagQueryCache>.Instance);

            const string key = "v1:query:a:none:0:0:50:0";
            var page = new Paged<QueryHitDto>
            {
                Items = [new QueryHitDto(Guid.NewGuid(), "file:///a", "file", "a")],
                Offset = 0,
                Limit = 50,
                Total = 1,
            };
            await cache.SetAsync(key, page, null, CancellationToken.None);
            var hit = await cache.TryGetAsync<Paged<QueryHitDto>>(key, CancellationToken.None);
            Assert.NotNull(hit);
            await cache.InvalidatePrefixAsync("v1:", CancellationToken.None);
            Assert.Null(await cache.TryGetAsync<Paged<QueryHitDto>>(key, CancellationToken.None));
        }
        finally
        {
            mux.Dispose();
        }
    }
}
