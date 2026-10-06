using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryServiceCacheHookTests
{
    [Fact]
    public async Task Unknown_positive_does_not_touch_cache()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        var r = await fx.GetQueryService().QueryAsync(QueryRequest.Parse("no-such-tag"), CancellationToken.None);
        Assert.True(r.IsSuccess);
        Assert.Empty(r.Data!.Items);
        Assert.Equal("Unknown positive term 'no-such-tag'; returning empty result.", r.Message);
        Assert.Empty(cache.Gets);
        Assert.Empty(cache.Sets);
    }

    [Fact]
    public async Task Expand_foo_is_bad_request()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetQueryService().QueryAsync(
            QueryRequest.Parse("工作", expand: "foo"),
            CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public async Task MaxDepth_above_server_max_is_expansion_limit_exceeded()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetQueryService().QueryAsync(
            QueryRequest.Parse("工作", expand: "child", maxDepth: "9"),
            CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ExpansionLimitExceeded, r.Code);
    }

    [Fact]
    public async Task ExpandedTagLimit_zero_is_bad_request()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetQueryService().QueryAsync(
            QueryRequest.Parse("工作", expand: "child", expandedTagLimit: "0"),
            CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public async Task Version_change_before_set_skips_cache_write()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        var tags = fx.GetTagService();
        var resources = fx.GetResourceService();
        var apply = fx.GetResourceTagService();

        var tag = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(tag.IsSuccess);
        var resource = await resources.CreateAsync(
            new CreateResourceRequest("file:///C:/query-bump.txt", "file", null),
            CancellationToken.None);
        Assert.True(resource.IsSuccess);
        var tagged = await apply.ApplyAsync(
            new TagApplyRequest(resource.Data!.Id, null, null, null, [TagApplyItem.FromAlias("工作")], null, false),
            CancellationToken.None);
        Assert.True(tagged.IsSuccess);

        cache.Gets.Clear();
        cache.Sets.Clear();

        var bumping = new BumpingDataVersionReader(fx.Db);
        var query = new QueryService(
            fx.Db,
            new QueryCachePipeline(
                cache,
                new QueryCacheSingleflight(),
                new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
                NullLogger<QueryCachePipeline>.Instance),
            Options.Create(new CacheOptions()),
            new QueryParser(),
            new QueryExpander(fx.Db),
            new QueryExecutor(fx.Db),
            bumping);

        var r = await query.QueryAsync(QueryRequest.Parse("工作"), CancellationToken.None);
        Assert.True(r.IsSuccess, r.Message);
        Assert.NotEmpty(r.Data!.Items);
        Assert.Empty(cache.Sets);
        Assert.NotEmpty(cache.Gets);
    }

    [Fact]
    public async Task Successful_empty_intersection_may_set()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        var tags = fx.GetTagService();
        await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        await tags.CreateAsync(new CreateTagRequest("生活", null, null, null), CancellationToken.None);

        cache.Gets.Clear();
        cache.Sets.Clear();

        var r = await fx.GetQueryService().QueryAsync(QueryRequest.Parse("工作 生活"), CancellationToken.None);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Empty(r.Data!.Items);
        Assert.Equal(0, r.Data.Total);
        Assert.NotEmpty(cache.Sets);
    }

    [Fact]
    public async Task Duplicate_positive_aliases_do_not_throw()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var resources = fx.GetResourceService();
        var apply = fx.GetResourceTagService();

        Assert.True((await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None)).IsSuccess);
        var resource = await resources.CreateAsync(
            new CreateResourceRequest("file:///C:/dup-alias.txt", "file", null),
            CancellationToken.None);
        Assert.True(resource.IsSuccess);
        Assert.True((await apply.ApplyAsync(
            new TagApplyRequest(resource.Data!.Id, null, null, null, [TagApplyItem.FromAlias("工作")], null, false),
            CancellationToken.None)).IsSuccess);

        var r = await fx.GetQueryService().QueryAsync(QueryRequest.Parse("工作 工作"), CancellationToken.None);
        Assert.True(r.IsSuccess, r.Message);
        Assert.Single(r.Data!.Items);
        Assert.Equal(resource.Data.Id, r.Data.Items[0].Id);
    }

    private sealed class BumpingDataVersionReader(ElDbContext db) : IDataVersionReader
    {
        private int _reads;

        public async Task<int> GetAsync(CancellationToken ct)
        {
            var version = await DatabaseInitializer.GetDataVersionAsync(db, ct);
            _reads++;
            // After Executor load, pipeline re-reads version — bump so Set is skipped.
            if (_reads >= 2)
                return version + 1;
            return version;
        }
    }
}
