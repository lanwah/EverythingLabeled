using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Resources;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class ResourceTagServiceTests
{
    [Fact]
    public async Task Apply_add_remove_is_idempotent_and_does_not_touch_resource_revision()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var resources = fx.GetResourceService();
        var apply = fx.GetResourceTagService();

        var tag = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
        Assert.True(tag.IsSuccess);
        var resource = await resources.CreateAsync(
            new CreateResourceRequest("file:///C:/a.txt", "file", null),
            CancellationToken.None);
        Assert.True(resource.IsSuccess);
        Assert.Equal(0, resource.Data!.Revision);
        var createdVersion = resource.Data.DataVersion;

        var add = new[] { TagApplyItem.FromAlias("工作") };
        var first = await apply.ApplyAsync(
            new TagApplyRequest(resource.Data.Id, null, null, null, add, null, false),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(0, first.Data!.Resource.Revision);
        Assert.Equal(createdVersion + 1, first.Data.DataVersion);
        Assert.Equal([tag.Data!.Id], first.Data.Added);
        Assert.Empty(first.Data.Removed);
        Assert.Equal(
            createdVersion + 1,
            await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));

        var second = await apply.ApplyAsync(
            new TagApplyRequest(resource.Data.Id, null, null, null, add, null, false),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Data.DataVersion, second.Data!.DataVersion);
        Assert.Equal(0, second.Data.Resource.Revision);
        Assert.Empty(second.Data.Added);
        Assert.Equal(
            first.Data.DataVersion,
            await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task CreateMissing_creates_tag_and_bind()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var apply = fx.GetResourceTagService();

        var result = await apply.ApplyAsync(
            new TagApplyRequest(
                null,
                "file:///C:/b.txt",
                "file",
                null,
                [TagApplyItem.FromAlias("新标")],
                null,
                true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!.CreatedTags);
        Assert.Equal("新标", result.Data.CreatedTags[0].Name);
        Assert.Equal(result.Data.CreatedTags[0].Id, Assert.Single(result.Data.Added));
        Assert.Equal(result.Data.CreatedTags[0].Id, Assert.Single(result.Data.Resource.Tags).Id);
        Assert.Equal(0, result.Data.Resource.Revision);
        Assert.Equal(1, result.Data.DataVersion);
    }

    [Fact]
    public async Task Https_uri_with_file_kind_is_validation_failed()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var result = await fx.GetResourceTagService().ApplyAsync(
            new TagApplyRequest(null, "https://example.com/a", "file", null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Code);
        var reason = result.Details?.GetType().GetProperty("reason")?.GetValue(result.Details);
        Assert.Equal("kind_uri_mismatch", reason);
        Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Add_plus_remove_over_100_is_batch_limit()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var add = Enumerable.Range(0, 50).Select(i => TagApplyItem.FromAlias("add" + i)).ToList();
        var remove = Enumerable.Range(0, 51).Select(i => TagApplyItem.FromAlias("rm" + i)).ToList();

        var result = await fx.GetResourceTagService().ApplyAsync(
            new TagApplyRequest(null, "file:///C:/c.txt", "file", null, add, remove, false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.BatchLimitExceeded, result.Code);
        Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }
}
