using EverythingLabeled.ExplorerHost;

namespace EverythingLabeled.ExplorerHost.Tests;

public sealed class SuggestQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_blank_returns_null(string? raw)
    {
        Assert.Null(SuggestQuery.Normalize(raw));
    }

    [Fact]
    public void Normalize_trims_and_keeps_nonempty()
    {
        Assert.Equal("work", SuggestQuery.Normalize("  work  "));
    }

    [Fact]
    public void Constants_match_spec()
    {
        Assert.Equal(250, SuggestQuery.DebounceMs);
        Assert.Equal(8, SuggestQuery.DefaultLimit);
    }
}
