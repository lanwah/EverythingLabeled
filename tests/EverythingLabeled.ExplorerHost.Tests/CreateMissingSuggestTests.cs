using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class CreateMissingSuggestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldCheck_blank_alias_is_false(string? alias)
    {
        Assert.False(CreateMissingSuggest.ShouldCheck(alias, ["work"]));
    }

    [Fact]
    public void ShouldCheck_exact_match_is_false()
    {
        Assert.False(CreateMissingSuggest.ShouldCheck("work", ["work", "workspace"]));
    }

    [Fact]
    public void ShouldCheck_exact_match_is_case_insensitive()
    {
        Assert.False(CreateMissingSuggest.ShouldCheck("Work", ["work"]));
    }

    [Fact]
    public void ShouldCheck_prefix_only_is_true()
    {
        Assert.True(CreateMissingSuggest.ShouldCheck("wor", ["work", "workspace"]));
    }

    [Fact]
    public void ShouldCheck_empty_suggest_is_true()
    {
        Assert.True(CreateMissingSuggest.ShouldCheck("newtag", Array.Empty<string>()));
    }
}
