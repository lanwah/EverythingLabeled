using System.Text;
using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;

namespace EverythingLabeled.Service.Tests;

public sealed class CacheEntrySizerTests
{
    [Fact]
    public void Empty_paged_query_hits_has_positive_size()
    {
        var page = new Paged<QueryHitDto>
        {
            Items = [],
            Offset = 0,
            Limit = 50,
            Total = 0
        };

        var size = CacheEntrySizer.Estimate("v1:query:a:none:0:0:50:0", page);
        Assert.True(size > 0);
    }

    [Fact]
    public void Chinese_display_name_counts_utf8_bytes()
    {
        const string displayName = "中文标签";
        var hit = new QueryHitDto(Guid.NewGuid(), "file:///a", "file", displayName);
        var utf8Bytes = Encoding.UTF8.GetByteCount(displayName);

        var size = CacheEntrySizer.Estimate("k", hit);
        Assert.True(size >= utf8Bytes);
    }

    [Fact]
    public void Longer_key_increases_estimate_for_same_value()
    {
        var value = new QueryHitDto(Guid.NewGuid(), "file:///a", "file", null);
        var shortKey = CacheEntrySizer.Estimate("k", value);
        var longKey = CacheEntrySizer.Estimate("key-with-more-chars", value);
        Assert.True(longKey > shortKey);
    }

    [Fact]
    public void Ascii_key_with_empty_string_meets_minimum_overhead()
    {
        const string key = "cache-key";
        var size = CacheEntrySizer.Estimate(key, "");
        Assert.True(size >= 192 + key.Length);
    }
}
