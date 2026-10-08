using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindQueryComposeTests
{
    [Fact]
    public void Compose_empty_returns_null()
    {
        Assert.Null(FindQueryCompose.Compose(Array.Empty<string>(), useOr: false));
        Assert.Null(FindQueryCompose.Compose(new[] { "  " }, useOr: true));
    }

    [Fact]
    public void Compose_and_joins_with_space()
    {
        var q = FindQueryCompose.Compose(new[] { "项目A", "图片" }, useOr: false);

        Assert.Equal("项目A 图片", q);
    }

    [Fact]
    public void Compose_or_joins_with_OR()
    {
        var q = FindQueryCompose.Compose(new[] { "项目A", "图片" }, useOr: true);

        Assert.Equal("项目A OR 图片", q);
    }

    [Fact]
    public void Compose_trims_and_skips_blanks()
    {
        var q = FindQueryCompose.Compose(new[] { "  a ", "", "b" }, useOr: false);

        Assert.Equal("a b", q);
    }

    [Fact]
    public void TryAdd_rejects_blank_and_duplicate_case_insensitive()
    {
        var tags = new List<string> { "Work" };

        Assert.False(FindQueryCompose.TryAdd(tags, "  "));
        Assert.False(FindQueryCompose.TryAdd(tags, "work"));
        Assert.True(FindQueryCompose.TryAdd(tags, "life"));
        Assert.Equal(new[] { "Work", "life" }, tags);
    }
}
