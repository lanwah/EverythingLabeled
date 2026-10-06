using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EverythingLabeled.Service.Tests;

public sealed class MemoryTagQueryCacheTests
{
    private static MemoryTagQueryCache Create(CacheOptions? o = null)
    {
        o ??= new CacheOptions();
        o.Validate();
        return new MemoryTagQueryCache(
            o,
            new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
            NullLogger<MemoryTagQueryCache>.Instance);
    }

    [Fact]
    public async Task Set_then_Get_roundtrips_payload()
    {
        using var cache = Create();
        var page = new Paged<QueryHitDto>
        {
            Items = [new QueryHitDto(Guid.NewGuid(), "file:///a", "file", "a")],
            Offset = 0, Limit = 50, Total = 1
        };
        await cache.SetAsync("v1:query:a:none:0:0:50:0", page, null, CancellationToken.None);
        var got = await cache.TryGetAsync<Paged<QueryHitDto>>("v1:query:a:none:0:0:50:0", CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal(page.Items[0].Uri, got!.Items[0].Uri);
    }

    [Fact]
    public async Task Ttl_does_not_slide_on_hit()
    {
        using var cache = Create();
        const string key = "v1:query:ttl:none:0:0:50:0";
        await cache.SetAsync(key, "x", new CacheEntryOptions(TimeSpan.FromMilliseconds(80)), CancellationToken.None);
        Assert.Equal("x", await cache.TryGetAsync<string>(key, CancellationToken.None));
        await Task.Delay(200);
        Assert.Null(await cache.TryGetAsync<string>(key, CancellationToken.None));
    }

    [Fact]
    public async Task MaxEntrySize_skips_set()
    {
        var options = new CacheOptions { SizeLimit = 10_000, MaxEntrySize = 100 };
        using var cache = Create(options);
        var huge = new string('x', 5000);
        await cache.SetAsync("v1:query:h:none:0:0:50:0", huge, null, CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<string>("v1:query:h:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_only_matching_version()
    {
        using var cache = Create();
        await cache.SetAsync("v1:query:a:none:0:0:50:0", "a", null, CancellationToken.None);
        await cache.SetAsync("v2:query:a:none:0:0:50:0", "b", null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v1:", CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
        Assert.Equal("b", await cache.TryGetAsync<string>("v2:query:a:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_prefix_is_noop()
    {
        using var cache = Create();
        await cache.SetAsync("v1:query:a:none:0:0:50:0", "a", null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("not-a-prefix", CancellationToken.None);
        Assert.Equal("a", await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Eviction_removes_index_no_ghost()
    {
        var options = new CacheOptions { SizeLimit = 400, MaxEntrySize = 400 };
        using var cache = Create(options);
        for (var i = 0; i < 50; i++)
            await cache.SetAsync($"v1:query:{i}:none:0:0:50:0", new string('y', 80), null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v1:", CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<string>("v1:query:0:none:0:0:50:0", CancellationToken.None));
    }
}
