using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class CacheVersionPrefixTests
{
    [Theory]
    [InlineData("v12:", 12)]
    [InlineData("v0:", 0)]
    public void Parses_valid_version_prefix(string prefix, int expected)
    {
        Assert.True(CacheVersionPrefix.TryParse(prefix, out var version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData("v12:query:")]
    [InlineData("12:")]
    [InlineData("v:")]
    [InlineData("vabc:")]
    [InlineData("")]
    public void Rejects_invalid_prefix(string prefix)
    {
        Assert.False(CacheVersionPrefix.TryParse(prefix, out _));
    }
}
