using EverythingLabeled.Service.Services.Caching;
using Microsoft.Extensions.Logging;

namespace EverythingLabeled.Service.Tests;

public sealed class QueryCacheMetricsTests
{
    [Fact]
    public void Hit_with_zero_flush_interval_logs_immediately()
    {
        var logger = new ListLogger<QueryCacheMetrics>();
        var metrics = new QueryCacheMetrics(logger) { FlushInterval = TimeSpan.Zero };

        metrics.Hit();

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("hit=1", StringComparison.Ordinal));
    }

    [Fact]
    public void Three_hits_with_zero_flush_interval_each_log_hit_one()
    {
        var logger = new ListLogger<QueryCacheMetrics>();
        var metrics = new QueryCacheMetrics(logger) { FlushInterval = TimeSpan.Zero };

        metrics.Hit();
        metrics.Hit();
        metrics.Hit();

        Assert.Equal(3, logger.Entries.Count(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("hit=1", StringComparison.Ordinal)));
    }
}
