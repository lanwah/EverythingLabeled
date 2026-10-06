using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class NullTagQueryCacheTests
{
    [Fact]
    public async Task TryGet_is_always_miss_and_Set_does_not_throw()
    {
        var cache = new NullTagQueryCache();
        Assert.Null(await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
        await cache.SetAsync("k", new { items = Array.Empty<int>() }, options: null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v0:", CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<object>("k", CancellationToken.None));
    }
}
