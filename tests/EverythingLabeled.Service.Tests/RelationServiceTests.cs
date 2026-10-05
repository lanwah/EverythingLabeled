using System.Text.Json;
using EverythingLabeled.Service.Data;
using EverythingLabeled.Service.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class RelationServiceTests
{
    [Fact]
    public async Task Child_second_parent_is_conflict()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var a = await CreateTagAsync(tags, "A");
        var b = await CreateTagAsync(tags, "B");
        var child = await CreateTagAsync(tags, "Child");

        var first = await relations.CreateAsync(new CreateRelationRequest(a, child, "child", null), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Message);
        var version = first.Data!.DataVersion;

        var second = await relations.CreateAsync(new CreateRelationRequest(b, child, "child", null), CancellationToken.None);
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCodes.RelationConflict, second.Code);
        Assert.Equal(version, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
        Assert.Equal(1, await fx.Db.TagRelations.CountAsync());
    }

    [Fact]
    public async Task Child_cycle_is_relation_cycle()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var a = await CreateTagAsync(tags, "A");
        var b = await CreateTagAsync(tags, "B");
        var c = await CreateTagAsync(tags, "C");

        Assert.True((await relations.CreateAsync(new CreateRelationRequest(a, b, "child", null), CancellationToken.None)).IsSuccess);
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(b, c, "child", null), CancellationToken.None)).IsSuccess);
        var version = await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None);

        var cycle = await relations.CreateAsync(new CreateRelationRequest(c, a, "child", null), CancellationToken.None);
        Assert.False(cycle.IsSuccess);
        Assert.Equal(ErrorCodes.RelationCycle, cycle.Code);
        Assert.Equal(version, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
        Assert.Equal(2, await fx.Db.TagRelations.CountAsync());
    }

    [Fact]
    public async Task Batch_reports_first_failing_index_and_writes_nothing()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var parent = await CreateTagAsync(tags, "Parent");
        var other = await CreateTagAsync(tags, "Other");
        var child = await CreateTagAsync(tags, "Kid");
        var version = await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None);

        var result = await relations.CreateBatchAsync(
            [
                new CreateRelationRequest(parent, child, "child", null),
                new CreateRelationRequest(other, child, "child", null),
                new CreateRelationRequest(parent, other, "related", null),
            ],
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.RelationConflict, result.Code);
        Assert.Equal(1, DetailIndex(result.Details));
        Assert.Equal(0, await fx.Db.TagRelations.CountAsync());
        Assert.Equal(version, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Reorder_same_order_does_not_bump()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var parent = await CreateTagAsync(tags, "P");
        var t1 = await CreateTagAsync(tags, "T1");
        var t2 = await CreateTagAsync(tags, "T2");
        var t3 = await CreateTagAsync(tags, "T3");

        Assert.True((await relations.CreateAsync(new CreateRelationRequest(parent, t1, "child", null), CancellationToken.None)).IsSuccess);
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(parent, t2, "child", null), CancellationToken.None)).IsSuccess);
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(parent, t3, "child", null), CancellationToken.None)).IsSuccess);

        var first = await relations.ReorderAsync(parent, "child", [t3, t1, t2], CancellationToken.None);
        Assert.True(first.IsSuccess, first.Message);
        Assert.Equal([t3, t1, t2], first.Data!.Items.Select(i => i.ToTagId).ToArray());
        Assert.Equal([0, 1, 2], first.Data.Items.Select(i => i.Seq).ToArray());
        var version = first.Data.DataVersion;

        var second = await relations.ReorderAsync(parent, "child", [t3, t1, t2], CancellationToken.None);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(version, second.Data!.DataVersion);
        Assert.Equal(version, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
    }

    [Fact]
    public async Task Related_both_directions_are_allowed()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var a = await CreateTagAsync(tags, "Ra");
        var b = await CreateTagAsync(tags, "Rb");

        var forward = await relations.CreateAsync(new CreateRelationRequest(a, b, "related", null), CancellationToken.None);
        var reverse = await relations.CreateAsync(new CreateRelationRequest(b, a, "related", null), CancellationToken.None);
        Assert.True(forward.IsSuccess, forward.Message);
        Assert.True(reverse.IsSuccess, reverse.Message);
        Assert.Equal(0, reverse.Data!.Revision);
    }

    [Fact]
    public async Task Self_loop_is_validation_failed()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var a = await CreateTagAsync(tags, "Self");

        var result = await relations.CreateAsync(new CreateRelationRequest(a, a, "child", null), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Code);
    }

    [Fact]
    public async Task List_requires_from_or_to()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var result = await fx.GetRelationService().ListAsync(null, null, null, new Paging(0, 50), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.RelationsFilterRequired, result.Code);
    }

    [Fact]
    public async Task Neighbors_missing_tag_is_not_found()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var result = await fx.GetRelationService().NeighborsAsync(
            Guid.NewGuid(),
            "both",
            null,
            new Paging(0, 50),
            CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, result.Code);
    }

    [Fact]
    public async Task Reorder_set_mismatch_is_validation_failed()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();
        var parent = await CreateTagAsync(tags, "Rp");
        var t1 = await CreateTagAsync(tags, "R1");
        var t2 = await CreateTagAsync(tags, "R2");
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(parent, t1, "child", null), CancellationToken.None)).IsSuccess);

        var result = await relations.ReorderAsync(parent, "child", [t1, t2], CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Code);
    }

    private static async Task<Guid> CreateTagAsync(TagService tags, string name)
    {
        var created = await tags.CreateAsync(new CreateTagRequest(name, null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Message);
        return created.Data!.Id;
    }

    private static int DetailIndex(object? details)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(details));
        return doc.RootElement.GetProperty("index").GetInt32();
    }
}
