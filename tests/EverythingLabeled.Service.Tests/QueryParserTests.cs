using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Query;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("OR 工作")]
    [InlineData("工作 OR")]
    [InlineData("工作 OR OR 生活")]
    [InlineData("-归档")]
    [InlineData("工作 OR -归档")]
    [InlineData("-")]
    public void Illegal_structure(string q)
    {
        var r = new QueryParser().Parse(q);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public void And_or_negatives()
    {
        var r = new QueryParser().Parse("工作 待读 OR 生活 -归档");
        Assert.True(r.IsSuccess);
        Assert.Equal(2, r.Data!.Clauses.Count);
        Assert.Equal(new[] { "工作", "待读" }, r.Data.Clauses[0].PositiveAliasNormalized);
        Assert.Empty(r.Data.Clauses[0].NegativeAliasNormalized);
        Assert.Equal(new[] { "生活" }, r.Data.Clauses[1].PositiveAliasNormalized);
        Assert.Equal(new[] { "归档" }, r.Data.Clauses[1].NegativeAliasNormalized);
        Assert.Equal("工作 待读 OR 生活 -归档", r.Data.Normalized);
    }

    [Fact]
    public void Double_dash_strips_one_leading_dash_as_negative()
    {
        var r = new QueryParser().Parse("工作 --x");
        Assert.True(r.IsSuccess);
        Assert.Equal(new[] { "工作" }, r.Data!.Clauses[0].PositiveAliasNormalized);
        Assert.Equal(new[] { "-x" }, r.Data.Clauses[0].NegativeAliasNormalized);
    }
}
