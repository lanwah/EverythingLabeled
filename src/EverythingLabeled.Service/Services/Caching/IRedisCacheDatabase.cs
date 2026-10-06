namespace EverythingLabeled.Service.Services.Caching;

internal interface IRedisCacheDatabase
{
    Task<byte[]?> StringGetAsync(string physicalKey, CancellationToken cancellationToken);
    Task PipelineSetAndIndexAsync(
        string physicalKey,
        byte[] payload,
        TimeSpan? ttl,
        string indexKey,
        string logicalMember,
        CancellationToken cancellationToken);
    Task SetRemoveAsync(string indexKey, string logicalMember, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> SetMembersAsync(string indexKey, CancellationToken cancellationToken);
    Task KeyDeleteAsync(IReadOnlyList<string> physicalOrIndexKeys, CancellationToken cancellationToken);
}
