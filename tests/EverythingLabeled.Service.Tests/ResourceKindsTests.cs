using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Resources;

namespace EverythingLabeled.Service.Tests;

public sealed class ResourceKindsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("all")]
    public void ParseQueryFilter_omit_blank_all_means_no_filter(string? raw)
    {
        var r = ResourceKinds.ParseQueryFilter(raw);
        Assert.True(r.IsSuccess);
        Assert.Null(r.Data);
        Assert.Equal("all", ResourceKinds.CacheKeyToken(r.Data));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("url")]
    public void ParseQueryFilter_known_kinds(string kind)
    {
        var r = ResourceKinds.ParseQueryFilter(kind);
        Assert.True(r.IsSuccess);
        Assert.Equal(kind, r.Data);
        Assert.Equal(kind, ResourceKinds.CacheKeyToken(r.Data));
    }

    [Fact]
    public void ParseQueryFilter_rejects_unknown()
    {
        var r = ResourceKinds.ParseQueryFilter("link");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }
}
