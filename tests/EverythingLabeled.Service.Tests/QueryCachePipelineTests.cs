using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryCachePipelineTests
{
    [Fact]
    public async Task Miss_sets_when_version_unchanged()
    {
        var cache = new RecordingCache();
        var loaded = await QueryCachePipeline.GetOrLoadAsync(
            cache,
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(1),
            _ => Task.FromResult("payload"),
            CancellationToken.None);

        Assert.Equal("payload", loaded);
        Assert.Contains("k", cache.Sets);
    }

    [Fact]
    public async Task Miss_skips_set_when_version_changed()
    {
        var cache = new RecordingCache();
        var loaded = await QueryCachePipeline.GetOrLoadAsync(
            cache,
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(2),
            _ => Task.FromResult("payload"),
            CancellationToken.None);

        Assert.Equal("payload", loaded);
        Assert.Empty(cache.Sets);
    }

    [Fact]
    public async Task Hit_does_not_call_load()
    {
        var cache = new RecordingCache();
        await cache.SetAsync("k", "cached", CancellationToken.None);
        cache.Sets.Clear();
        var loadCalls = 0;

        var loaded = await QueryCachePipeline.GetOrLoadAsync(
            cache,
            "k",
            versionAtKey: 1,
            _ => Task.FromResult(1),
            _ =>
            {
                loadCalls++;
                return Task.FromResult("fresh");
            },
            CancellationToken.None);

        Assert.Equal("cached", loaded);
        Assert.Equal(0, loadCalls);
        Assert.Empty(cache.Sets);
    }
}
