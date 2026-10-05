using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Tests;

public sealed class PagingTests
{
    [Fact]
    public void Omitted_values_default_to_offset_zero_limit_50()
    {
        var r = Paging.TryParse(null, null);
        Assert.True(r.IsSuccess);
        Assert.Equal(0, r.Data.Offset);
        Assert.Equal(Paging.DefaultLimit, r.Data.Limit);
    }

    [Theory]
    [InlineData("x", "50")]
    [InlineData("0", "1.5")]
    [InlineData("-1", "50")]
    [InlineData("0", "-1")]
    [InlineData("0", "0")]
    [InlineData("0", "1025")]
    public void Rejects_invalid_paging_without_clamping(string? offset, string? limit)
    {
        var r = Paging.TryParse(offset, limit);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public void Accepts_max_limit()
    {
        var r = Paging.TryParse("10", "1024");
        Assert.True(r.IsSuccess);
        Assert.Equal(10, r.Data.Offset);
        Assert.Equal(1024, r.Data.Limit);
    }
}
