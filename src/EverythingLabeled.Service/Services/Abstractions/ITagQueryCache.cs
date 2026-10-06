namespace EverythingLabeled.Service.Services.Abstractions;

public interface ITagQueryCache
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class;
    Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken);
}
