using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryCachePipelineTests
{
    private static QueryCachePipeline Create(ITagQueryCache cache) =>
        new(
            cache,
            new QueryCacheSingleflight(),
            new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
            NullLogger<QueryCachePipeline>.Instance);

    [Fact]
    public async Task Miss_sets_when_version_unchanged()
    {
        var cache = new RecordingCache();
        var loaded = await Create(cache).GetOrLoadAsync(
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(1),
            _ => Task.FromResult("payload"),
            options: null,
            CancellationToken.None);

        Assert.Equal("payload", loaded);
        Assert.Contains("k", cache.Sets);
    }

    [Fact]
    public async Task Miss_skips_set_when_version_changed()
    {
        var cache = new RecordingCache();
        var loaded = await Create(cache).GetOrLoadAsync(
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(2),
            _ => Task.FromResult("payload"),
            options: null,
            CancellationToken.None);

        Assert.Equal("payload", loaded);
        Assert.Empty(cache.Sets);
    }

    [Fact]
    public async Task Hit_does_not_call_load()
    {
        var cache = new RecordingCache();
        await cache.SetAsync("k", "cached", options: null, CancellationToken.None);
        cache.Sets.Clear();
        var loadCalls = 0;

        var loaded = await Create(cache).GetOrLoadAsync(
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(1),
            _ =>
            {
                loadCalls++;
                return Task.FromResult("fresh");
            },
            options: null,
            CancellationToken.None);

        Assert.Equal("cached", loaded);
        Assert.Equal(0, loadCalls);
        Assert.Empty(cache.Sets);
    }

    [Fact]
    public async Task Same_key_loads_once()
    {
        var cache = new RecordingCache();
        var pipeline = Create(cache);
        var loadCalls = 0;
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Load(CancellationToken _)
        {
            Interlocked.Increment(ref loadCalls);
            return await gate.Task;
        }

        var first = pipeline.GetOrLoadAsync("k", 1, _ => Task.FromResult(1), Load, null, CancellationToken.None);
        var second = pipeline.GetOrLoadAsync("k", 1, _ => Task.FromResult(1), Load, null, CancellationToken.None);
        gate.SetResult("once");

        Assert.Equal("once", await first);
        Assert.Equal("once", await second);
        Assert.Equal(1, loadCalls);
    }

    [Fact]
    public async Task Waiters_ignore_request_cancellation()
    {
        var cache = new RecordingCache();
        var pipeline = Create(cache);
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = pipeline.GetOrLoadAsync(
            "k",
            1,
            _ => Task.FromResult(1),
            _ => gate.Task,
            null,
            cts.Token);

        gate.SetResult("still-loaded");
        Assert.Equal("still-loaded", await task);
    }

    [Fact]
    public async Task Empty_paged_list_is_still_set()
    {
        var cache = new RecordingCache();
        var page = new Paged<QueryHitDto>
        {
            Items = [],
            Offset = 0,
            Limit = 50,
            Total = 0,
        };

        var loaded = await Create(cache).GetOrLoadAsync(
            "k",
            1,
            _ => Task.FromResult(1),
            _ => Task.FromResult(page),
            null,
            CancellationToken.None);

        Assert.Same(page, loaded);
        Assert.Contains("k", cache.Sets);
    }

    [Fact]
    public async Task Snapshot_paged_items_are_not_the_mutable_list()
    {
        var cache = new RecordingCache();
        var mutable = new List<QueryHitDto>
        {
            new(Guid.NewGuid(), "file:///a", "file", "a"),
        };
        var page = new Paged<QueryHitDto>
        {
            Items = mutable,
            Offset = 0,
            Limit = 50,
            Total = 1,
        };

        await Create(cache).GetOrLoadAsync(
            "k",
            1,
            _ => Task.FromResult(1),
            _ => Task.FromResult(page),
            null,
            CancellationToken.None);

        mutable.Clear();
        var stored = Assert.IsType<Paged<QueryHitDto>>(cache.Store["k"]);
        Assert.NotSame(mutable, stored.Items);
        Assert.Single(stored.Items);
    }
}
