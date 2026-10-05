using EverythingLabeled.Service.Services.Query;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryNormalizerTests
{
    [Fact]
    public void Preserves_in_clause_order_and_canonical_OR()
    {
        Assert.Equal("工作 OR 生活 -归档", QueryNormalizer.Normalize("  工作   or  生活 -归档 "));
        Assert.Equal("待读 工作", QueryNormalizer.Normalize("待读 工作"));
        Assert.NotEqual(QueryNormalizer.Normalize("工作 待读"), QueryNormalizer.Normalize("待读 工作"));
    }
}
