using StackExchange.Redis;

namespace EverythingLabeled.Service.Services.Caching;

internal sealed class StackExchangeRedisDatabase(IDatabase db) : IRedisCacheDatabase
{
    public async Task<byte[]?> StringGetAsync(string physicalKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await db.StringGetAsync(physicalKey).ConfigureAwait(false);
        return value.IsNull ? null : (byte[]?)value;
    }

    public async Task PipelineSetAndIndexAsync(
        string physicalKey,
        byte[] payload,
        TimeSpan? ttl,
        string indexKey,
        string logicalMember,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batch = db.CreateBatch();
        var setTask = ttl is { } expiry
            ? batch.StringSetAsync(physicalKey, payload, expiry)
            : batch.StringSetAsync(physicalKey, payload);
        Task? addTask = null;
        if (indexKey.Length > 0)
            addTask = batch.SetAddAsync(indexKey, logicalMember);
        batch.Execute();
        await setTask.ConfigureAwait(false);
        if (addTask is not null)
            await addTask.ConfigureAwait(false);
    }

    public async Task SetRemoveAsync(string indexKey, string logicalMember, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await db.SetRemoveAsync(indexKey, logicalMember).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SetMembersAsync(string indexKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var members = await db.SetMembersAsync(indexKey).ConfigureAwait(false);
        return members.Select(m => (string)m!).ToArray();
    }

    public async Task KeyDeleteAsync(IReadOnlyList<string> physicalOrIndexKeys, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (physicalOrIndexKeys.Count == 0)
            return;
        var keys = physicalOrIndexKeys.Select(k => (RedisKey)k).ToArray();
        await db.KeyDeleteAsync(keys).ConfigureAwait(false);
    }
}
