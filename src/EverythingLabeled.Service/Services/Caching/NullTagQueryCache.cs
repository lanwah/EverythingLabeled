using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Caching;

public sealed class NullTagQueryCache : ITagQueryCache
{
    public Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
        Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class =>
        Task.CompletedTask;

    public Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
