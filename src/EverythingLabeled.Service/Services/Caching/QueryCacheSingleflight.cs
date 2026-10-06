using System.Collections.Concurrent;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class QueryCacheSingleflight
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _flights = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> load) where T : class
    {
        var lazy = _flights.GetOrAdd(
            key,
            static (_, l) => new Lazy<Task<object>>(() => Invoke(l)),
            load);
        try
        {
            return (T)await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<object>>>>)_flights)
                .Remove(new KeyValuePair<string, Lazy<Task<object>>>(key, lazy));
        }
    }

    private static async Task<object> Invoke<T>(Func<Task<T>> load) where T : class =>
        await load().ConfigureAwait(false);
}
