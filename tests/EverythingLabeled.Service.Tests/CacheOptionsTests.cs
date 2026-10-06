using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class CacheOptionsTests
{
    [Fact]
    public void Default_memory_options_validate()
    {
        var options = new CacheOptions();
        options.Validate();
    }

    [Fact]
    public void Memory_ignores_redis_connection_when_present()
    {
        var options = new CacheOptions { RedisConnection = "localhost:6379" };
        options.Validate();
    }

    [Theory]
    [InlineData("redis")]
    [InlineData("Redis")]
    public void Redis_without_connection_throws(string provider)
    {
        var options = new CacheOptions { Provider = provider };
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("RedisConnection", ex.Message);
    }

    [Fact]
    public void Disk_provider_throws()
    {
        var options = new CacheOptions { Provider = "Disk" };
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("Disk", ex.Message);
    }

    [Fact]
    public void SizeLimit_zero_throws()
    {
        var options = new CacheOptions { SizeLimit = 0 };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void MaxEntrySize_zero_throws()
    {
        var options = new CacheOptions { MaxEntrySize = 0 };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void MaxEntrySize_above_size_limit_throws()
    {
        var options = new CacheOptions { SizeLimit = 100, MaxEntrySize = 101 };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void SuggestTtl_zero_throws()
    {
        var options = new CacheOptions { SuggestTtl = TimeSpan.Zero };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Redis_blank_key_prefix_throws()
    {
        var options = new CacheOptions
        {
            Provider = "Redis",
            RedisConnection = "localhost:6379",
            KeyPrefix = " "
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Redis_zero_connect_timeout_throws()
    {
        var options = new CacheOptions
        {
            Provider = "Redis",
            RedisConnection = "localhost:6379",
            RedisConnectTimeout = TimeSpan.Zero
        };
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }
}
