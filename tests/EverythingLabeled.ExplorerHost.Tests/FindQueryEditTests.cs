using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class FindQueryEditTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("work ")]
    [InlineData("a b  ")]
    public void LastTokenForSuggest_blank_or_trailing_space_returns_null(string? raw)
    {
        Assert.Null(FindQueryEdit.LastTokenForSuggest(raw));
    }

    [Theory]
    [InlineData("gz", "gz")]
    [InlineData("  gz", "gz")]
    [InlineData("work gz", "gz")]
    [InlineData("a OR b", "b")]
    public void LastTokenForSuggest_returns_incomplete_last_token(string raw, string expected)
    {
        Assert.Equal(expected, FindQueryEdit.LastTokenForSuggest(raw));
    }

    [Theory]
    [InlineData("gz", "工作", "工作 ")]
    [InlineData("foo gz", "工作", "foo 工作 ")]
    [InlineData("foo ", "工作", "foo 工作 ")]
    [InlineData("", "工作", "工作 ")]
    [InlineData("  ", "工作", "工作 ")]
    public void ReplaceLastToken_fills_alias_and_trailing_space(string raw, string alias, string expected)
    {
        Assert.Equal(expected, FindQueryEdit.ReplaceLastToken(raw, alias));
    }
}
