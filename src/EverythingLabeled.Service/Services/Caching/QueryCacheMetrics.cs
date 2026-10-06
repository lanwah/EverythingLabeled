using System.Diagnostics;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class QueryCacheMetrics(ILogger<QueryCacheMetrics> logger)
{
    public const int FlushIntervalSeconds = 60;

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(FlushIntervalSeconds);

    private long _hit;
    private long _miss;
    private long _skipMaxEntry;
    private long _invalidate;
    private long _lastFlushTimestamp = Stopwatch.GetTimestamp();

    public void Hit()
    {
        Interlocked.Increment(ref _hit);
        MaybeFlush();
    }

    public void Miss()
    {
        Interlocked.Increment(ref _miss);
        MaybeFlush();
    }

    public void SkipMaxEntry()
    {
        Interlocked.Increment(ref _skipMaxEntry);
        MaybeFlush();
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _invalidate);
        MaybeFlush();
    }

    private void MaybeFlush()
    {
        var now = Stopwatch.GetTimestamp();
        var lastFlush = Volatile.Read(ref _lastFlushTimestamp);
        if (Stopwatch.GetElapsedTime(lastFlush, now) < FlushInterval)
            return;

        if (Interlocked.CompareExchange(ref _lastFlushTimestamp, now, lastFlush) != lastFlush)
            return;

        var hit = Interlocked.Exchange(ref _hit, 0);
        var miss = Interlocked.Exchange(ref _miss, 0);
        var skip = Interlocked.Exchange(ref _skipMaxEntry, 0);
        var invalidate = Interlocked.Exchange(ref _invalidate, 0);

        logger.LogInformation(
            "Query cache stats hit={Hit} miss={Miss} skip_max_entry={Skip} invalidate={Invalidate}",
            hit,
            miss,
            skip,
            invalidate);
    }
}
