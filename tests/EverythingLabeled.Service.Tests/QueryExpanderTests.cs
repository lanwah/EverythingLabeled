using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Query;
using EverythingLabeled.Service.Services.Relations;
using EverythingLabeled.Service.Services.Tags;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryExpanderTests
{
    [Fact]
    public async Task Max_depth_truncates_without_422()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();

        var a = await CreateTagAsync(tags, "A");
        var b = await CreateTagAsync(tags, "B");
        var c = await CreateTagAsync(tags, "C");
        var d = await CreateTagAsync(tags, "D");

        Assert.True((await relations.CreateAsync(new CreateRelationRequest(a, b, RelationGraph.Child, null), CancellationToken.None)).IsSuccess);
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(b, c, RelationGraph.Child, null), CancellationToken.None)).IsSuccess);
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(c, d, RelationGraph.Child, null), CancellationToken.None)).IsSuccess);

        var parsed = new QueryParser().Parse("A");
        Assert.True(parsed.IsSuccess);
        var resolved = new List<ResolvedTerm> { new(true, "a", 0, a) };
        var expander = new QueryExpander(fx.Db);
        var result = await expander.ExpandAsync(parsed.Data!, resolved, maxDepth: 1, expandedTagLimit: 512, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.Data!.Truncated);
        Assert.Equal(2, result.Data.AllExpandedTagIds.Count); // A + B
        Assert.Contains(a, result.Data.AllExpandedTagIds);
        Assert.Contains(b, result.Data.AllExpandedTagIds);
        Assert.DoesNotContain(c, result.Data.AllExpandedTagIds);
    }

    [Fact]
    public async Task Expanded_tag_limit_exceeded_is_422()
    {
        await using var fx = await SqliteServiceFixture.CreateAsync();
        var tags = fx.GetTagService();
        var relations = fx.GetRelationService();

        var parent = await CreateTagAsync(tags, "Parent");
        var child = await CreateTagAsync(tags, "Child");
        Assert.True((await relations.CreateAsync(new CreateRelationRequest(parent, child, RelationGraph.Child, null), CancellationToken.None)).IsSuccess);

        var parsed = new QueryParser().Parse("Parent");
        Assert.True(parsed.IsSuccess);
        var resolved = new List<ResolvedTerm> { new(true, "parent", 0, parent) };
        var expander = new QueryExpander(fx.Db);
        var result = await expander.ExpandAsync(parsed.Data!, resolved, maxDepth: 8, expandedTagLimit: 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ExpansionLimitExceeded, result.Code);
    }

    private static async Task<Guid> CreateTagAsync(TagService tags, string name)
    {
        var created = await tags.CreateAsync(new CreateTagRequest(name, null, null, null), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Message);
        return created.Data!.Id;
    }
}
