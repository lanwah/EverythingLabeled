using StackExchange.Redis;

namespace EverythingLabeled.Service.Services.Caching;

public static class RedisConnection
{
    public static ConnectionMultiplexer Connect(CacheOptions options)
    {
        var cfg = ConfigurationOptions.Parse(options.RedisConnection!);
        cfg.AbortOnConnectFail = true;
        cfg.ConnectTimeout = (int)options.RedisConnectTimeout.TotalMilliseconds;
        return ConnectionMultiplexer.Connect(cfg);
    }
}
