using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryCacheKeyFactoryTests
{
    [Fact]
    public void Query_unexpanded_uses_none_and_zero_placeholders()
    {
        var key = QueryCacheKeyFactory.ForQuery(3, "query", "工作 OR 生活 -归档", "none", 0, 0, 50, 0);
        Assert.Equal("v3:query:工作 OR 生活 -归档:none:0:0:50:0", key);
    }

    [Fact]
    public void Query_key_is_opaque_equality_and_may_contain_colon_in_normalized_query()
    {
        var key = QueryCacheKeyFactory.ForQuery(1, "preview", "proj:foo", "child", 8, 512, 16, 0);
        Assert.Equal("v1:preview:proj:foo:child:8:512:16:0", key);
        Assert.DoesNotContain("split", key); // 工厂本身不得提供 Split 解析 API
    }

    [Fact]
    public void Suggest_key_has_no_expand_dimensions()
    {
        var key = QueryCacheKeyFactory.ForSuggest(2, "gz", 20);
        Assert.Equal("v2:suggest:gz:20", key);
    }
}
