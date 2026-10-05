using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;

namespace EverythingLabeled.Service.Tests;

public sealed class RecordingCache : ITagQueryCache
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _invalidateAttempted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Gets { get; } = [];
    public List<string> Sets { get; } = [];
    public List<string> InvalidatedPrefixes { get; } = [];
    public Dictionary<string, object> Store { get; } = new(StringComparer.Ordinal);
    public Exception? InvalidateException { get; set; }

    public Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        Gets.Add(key);
        if (Store.TryGetValue(key, out var value))
            return Task.FromResult(value as T);
        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
    {
        Sets.Add(key);
        Store[key] = value;
        return Task.CompletedTask;
    }

    public Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            if (InvalidateException is not null)
                throw InvalidateException;
            lock (_gate)
                InvalidatedPrefixes.Add(prefix);
            return Task.CompletedTask;
        }
        finally
        {
            _invalidateAttempted.TrySetResult();
        }
    }

    public Task WaitForInvalidateAsync() =>
        _invalidateAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public async Task WaitForInvalidateCountAsync(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate)
            {
                if (InvalidatedPrefixes.Count >= count)
                    return;
            }

            await Task.Delay(10);
        }

        lock (_gate)
            Assert.True(InvalidatedPrefixes.Count >= count,
                $"Timed out waiting for {count} invalidations; saw [{string.Join(", ", InvalidatedPrefixes)}].");
    }
}
