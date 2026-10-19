using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class TagServiceTests
{
    [Fact]
    public async Task Create_without_aliases_inserts_default_alias_revision_zero()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(0, created.Data!.Revision);
        Assert.Equal("gz", created.Data.NameInitials);
        Assert.Equal(new[] { "工作" }, created.Data.Aliases);
        Assert.Equal(1, created.Data.DataVersion);
    }

    [Fact]
    public async Task Create_illegal_name_as_default_alias_fails_without_bump()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetTagService().CreateAsync(new CreateTagRequest("or", null, null, null), CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
        Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_last_alias_is_validation_failed()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var r = await tags.DeleteAliasAsync(created.Data!.Id, "工作", CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
        Assert.Contains("last_alias", System.Text.Json.JsonSerializer.Serialize(r.Details));
        Assert.Equal(1, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Same_value_patch_does_not_bump()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var version = created.Data!.DataVersion;

        var patched = await tags.PatchAsync(
            created.Data.Id,
            new PatchTagRequest("工作", null, null),
            CancellationToken.None);
        Assert.True(patched.IsSuccess);
        Assert.Equal(version, patched.Data!.DataVersion);
        Assert.Equal(0, patched.Data.Revision);
        Assert.Equal(version, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Prefix_search_does_not_use_substring()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("abcdef", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var miss = await tags.ListAsync(null, "cde", new Paging(0, 50), CancellationToken.None);
        Assert.True(miss.IsSuccess);
        Assert.Empty(miss.Data!.Items);

        var hit = await tags.ListAsync(null, "abc", new Paging(0, 50), CancellationToken.None);
        Assert.True(hit.IsSuccess);
        Assert.Single(hit.Data!.Items);
        Assert.Equal("abcdef", hit.Data.Items[0].Name);
    }

    [Fact]
    public async Task Suggest_offset_greater_than_zero_is_bad_request()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetSuggestService().SuggestAsync("abc", new Paging(1, 50), CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public async Task Create_duplicate_normalized_aliases_is_alias_conflict_without_bump()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetTagService().CreateAsync(
            new CreateTagRequest("Work", null, null, ["Foo", "foo"]),
            CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.AliasConflict, r.Code);
        Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Create_with_null_name_fails_without_throwing()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var r = await fx.GetTagService().CreateAsync(
            new CreateTagRequest(null, null, null, null),
            CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.True(
            r.Code is ErrorCodes.ValidationFailed or ErrorCodes.BadRequest,
            $"expected validation_failed or bad_request, got {r.Code}");
        Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task AddAlias_with_null_alias_fails_without_throwing()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var r = await tags.AddAliasAsync(created.Data!.Id, null, CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.True(
            r.Code is ErrorCodes.ValidationFailed or ErrorCodes.BadRequest,
            $"expected validation_failed or bad_request, got {r.Code}");
        Assert.Equal(created.Data.DataVersion, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Rename_swaps_matching_alias_and_name_keeps_other_aliases()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(
            new CreateTagRequest("工做", null, null, ["工做", "work"]),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var renamed = await tags.RenameAsync(new RenameTagRequest("工做", "工作"), CancellationToken.None);
        Assert.True(renamed.IsSuccess);
        Assert.Equal("工作", renamed.Data!.Name);
        Assert.Contains("工作", renamed.Data.Aliases);
        Assert.Contains("work", renamed.Data.Aliases);
        Assert.DoesNotContain("工做", renamed.Data.Aliases);
        Assert.True(renamed.Data.DataVersion > created.Data!.DataVersion);
    }

    [Fact]
    public async Task Rename_same_normalized_alias_does_not_bump()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var created = await tags.CreateAsync(new CreateTagRequest("Work", null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var renamed = await tags.RenameAsync(new RenameTagRequest("Work", "work"), CancellationToken.None);
        Assert.True(renamed.IsSuccess);
        Assert.Equal(created.Data!.DataVersion, renamed.Data!.DataVersion);
        Assert.Equal(created.Data.DataVersion, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Rename_to_alias_owned_by_other_tag_is_conflict()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        Assert.True((await tags.CreateAsync(new CreateTagRequest("甲", null, null, null), CancellationToken.None)).IsSuccess);
        Assert.True((await tags.CreateAsync(new CreateTagRequest("乙", null, null, null), CancellationToken.None)).IsSuccess);

        var r = await tags.RenameAsync(new RenameTagRequest("甲", "乙"), CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.AliasConflict, r.Code);
    }
}
