using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Data.Entities;
using EverythingLabeled.Service.Services;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class UnitOfWorkTests
{
    [Fact]
    public async Task Concurrent_dirty_commits_monotonically_increment()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        fx.Db.Tags.Add(MinimalTag("a"));
        fx.Uow.MarkDirty();
        await fx.Uow.CommitAsync(CancellationToken.None); // version 1

        var bumped = await Task.WhenAll(BumpAsync(fx.DbPath), BumpAsync(fx.DbPath));
        await using var check = fx.OpenNewContext();
        Assert.Equal(3, await DatabaseInitializer.GetDataVersionAsync(check, CancellationToken.None));
        Assert.Equal(new[] { 2, 3 }, bumped.OrderBy(v => v).ToArray());
    }

    [Fact]
    public async Task Concurrent_dirty_commits_invalidate_each_vacated_prefix()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync();
        fx.Db.Tags.Add(MinimalTag("a"));
        fx.Uow.MarkDirty();
        await fx.Uow.CommitAsync(CancellationToken.None); // version 1

        await Task.WhenAll(BumpAsync(fx.DbPath, cache), BumpAsync(fx.DbPath, cache));
        await cache.WaitForInvalidateCountAsync(2);

        Assert.Contains("v1:", cache.InvalidatedPrefixes);
        Assert.Contains("v2:", cache.InvalidatedPrefixes);
        Assert.Equal(2, cache.InvalidatedPrefixes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Clean_commit_does_not_bump_or_invalidate()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        var v = await fx.Uow.CommitAsync(CancellationToken.None);
        Assert.Equal(0, v);
        Assert.Empty(cache.InvalidatedPrefixes);
    }

    [Fact]
    public async Task Dirty_commit_invalidates_previous_prefix()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        fx.Db.Tags.Add(MinimalTag("a"));
        fx.Uow.MarkDirty();
        await fx.Uow.CommitAsync(CancellationToken.None);
        await cache.WaitForInvalidateAsync();
        Assert.Contains("v0:", cache.InvalidatedPrefixes);
    }

    [Fact]
    public async Task Dirty_commit_succeeds_when_invalidate_throws()
    {
        var cache = new RecordingCache { InvalidateException = new InvalidOperationException("cache down") };
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        fx.Db.Tags.Add(MinimalTag("a"));
        fx.Uow.MarkDirty();
        var v = await fx.Uow.CommitAsync(CancellationToken.None);
        Assert.Equal(1, v);
        await cache.WaitForInvalidateAsync();
        Assert.Empty(cache.InvalidatedPrefixes);
    }

    private static Task<int> BumpAsync(string dbPath) =>
        BumpAsync(dbPath, new NullTagQueryCache());

    private static async Task<int> BumpAsync(string dbPath, ITagQueryCache cache)
    {
        await using var db = SqliteServiceFixture.OpenContext(dbPath);
        var uow = new UnitOfWork(db, cache);
        db.Tags.Add(MinimalTag(Guid.NewGuid().ToString("N")));
        uow.MarkDirty();
        return await uow.CommitAsync(CancellationToken.None);
    }

    private static Tag MinimalTag(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NameInitials = "x",
        NameNormalized = name.ToLowerInvariant(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Revision = 0,
    };
}
