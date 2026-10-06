namespace EverythingLabeled.Service.Services.Abstractions;

public sealed record CacheEntryOptions(TimeSpan? AbsoluteExpirationRelativeToNow);
