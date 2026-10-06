using EverythingLabeled.Service.Services.Abstractions;
using EverythingLabeled.Service.Services.Caching;
using EverythingLabeled.Service.Services.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace EverythingLabeled.Service.Tests;

public sealed class RedisTagQueryCacheTests
{
    private static RedisTagQueryCache Create(FakeRedisCacheDatabase db, CacheOptions? options = null)
    {
        options ??= new CacheOptions { Provider = "Redis", RedisConnection = "localhost:6379" };
        options.Validate();
        return new RedisTagQueryCache(
            options,
            db,
            new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
            NullLogger<RedisTagQueryCache>.Instance);
    }

    [Fact]
    public async Task Set_then_Get_roundtrips_paged_json()
    {
        var db = new FakeRedisCacheDatabase();
        var cache = Create(db);
        var page = new Paged<QueryHitDto>
        {
            Items = [new QueryHitDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "file:///a", "file", "a")],
            Offset = 0,
            Limit = 50,
            Total = 1,
        };

        await cache.SetAsync("v12:query:a:none:0:0:50:0", page, null, CancellationToken.None);
        var got = await cache.TryGetAsync<Paged<QueryHitDto>>("v12:query:a:none:0:0:50:0", CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal("file:///a", got!.Items[0].Uri);
        Assert.Equal("a", got.Items[0].DisplayName);
    }

    [Fact]
    public async Task Set_pipelines_physical_set_before_sadd()
    {
        var db = new FakeRedisCacheDatabase();
        var cache = Create(db);
        const string logical = "v12:query:a:none:0:0:50:0";
        await cache.SetAsync(logical, "payload", null, CancellationToken.None);

        Assert.Equal(["SET el:qc:v12:query:a:none:0:0:50:0", "SADD el:qc:idx:12 v12:query:a:none:0:0:50:0"], db.Ops);
        Assert.StartsWith("el:qc:", db.Ops[0][4..]);
        Assert.Contains("SADD el:qc:idx:12 " + logical, db.Ops);
    }

    [Fact]
    public async Task Get_miss_srems_logical_member()
    {
        var db = new FakeRedisCacheDatabase();
        var cache = Create(db);
        const string logical = "v12:query:missing:none:0:0:50:0";

        Assert.Null(await cache.TryGetAsync<string>(logical, CancellationToken.None));
        Assert.Contains("GET el:qc:v12:query:missing:none:0:0:50:0", db.Ops);
        Assert.Contains("SREM el:qc:idx:12 " + logical, db.Ops);
    }

    [Fact]
    public async Task Invalidate_deletes_physical_and_index()
    {
        var db = new FakeRedisCacheDatabase();
        var cache = Create(db);
        const string logical = "v12:query:a:none:0:0:50:0";
        await cache.SetAsync(logical, "a", null, CancellationToken.None);
        db.Ops.Clear();

        await cache.InvalidatePrefixAsync("v12:", CancellationToken.None);

        Assert.Contains("SMEMBERS el:qc:idx:12", db.Ops);
        Assert.Contains("DEL el:qc:v12:query:a:none:0:0:50:0", db.Ops);
        Assert.Contains("DEL el:qc:idx:12", db.Ops);
        Assert.Null(await cache.TryGetAsync<string>(logical, CancellationToken.None));
    }

    [Fact]
    public async Task MaxEntrySize_skips_set()
    {
        var db = new FakeRedisCacheDatabase();
        var options = new CacheOptions
        {
            Provider = "Redis",
            RedisConnection = "localhost:6379",
            SizeLimit = 10_000,
            MaxEntrySize = 8,
        };
        var cache = Create(db, options);
        await cache.SetAsync("v1:query:h:none:0:0:50:0", "too-big-value", null, CancellationToken.None);

        Assert.DoesNotContain(db.Ops, op => op.StartsWith("SET ", StringComparison.Ordinal));
        Assert.Null(await cache.TryGetAsync<string>("v1:query:h:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_prefix_does_not_delete()
    {
        var db = new FakeRedisCacheDatabase();
        var cache = Create(db);
        await cache.SetAsync("v1:query:a:none:0:0:50:0", "a", null, CancellationToken.None);
        db.Ops.Clear();

        await cache.InvalidatePrefixAsync("not-a-prefix", CancellationToken.None);

        Assert.DoesNotContain(db.Ops, op => op.StartsWith("DEL ", StringComparison.Ordinal));
        Assert.Equal("a", await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
    }
}

internal sealed class FakeRedisCacheDatabase : IRedisCacheDatabase
{
    private readonly Dictionary<string, byte[]> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _sets = new(StringComparer.Ordinal);

    public List<string> Ops { get; } = [];

    public Task<byte[]?> StringGetAsync(string physicalKey, CancellationToken cancellationToken)
    {
        Ops.Add("GET " + physicalKey);
        _strings.TryGetValue(physicalKey, out var value);
        return Task.FromResult(value);
    }

    public Task PipelineSetAndIndexAsync(
        string physicalKey,
        byte[] payload,
        TimeSpan? ttl,
        string indexKey,
        string logicalMember,
        CancellationToken cancellationToken)
    {
        Ops.Add("SET " + physicalKey);
        _strings[physicalKey] = payload;
        if (indexKey.Length > 0)
        {
            Ops.Add("SADD " + indexKey + " " + logicalMember);
            if (!_sets.TryGetValue(indexKey, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _sets[indexKey] = set;
            }

            set.Add(logicalMember);
        }

        return Task.CompletedTask;
    }

    public Task SetRemoveAsync(string indexKey, string logicalMember, CancellationToken cancellationToken)
    {
        Ops.Add("SREM " + indexKey + " " + logicalMember);
        if (_sets.TryGetValue(indexKey, out var set))
            set.Remove(logicalMember);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SetMembersAsync(string indexKey, CancellationToken cancellationToken)
    {
        Ops.Add("SMEMBERS " + indexKey);
        if (!_sets.TryGetValue(indexKey, out var set))
            return Task.FromResult<IReadOnlyList<string>>([]);
        return Task.FromResult<IReadOnlyList<string>>([.. set]);
    }

    public Task KeyDeleteAsync(IReadOnlyList<string> physicalOrIndexKeys, CancellationToken cancellationToken)
    {
        foreach (var key in physicalOrIndexKeys)
        {
            Ops.Add("DEL " + key);
            _strings.Remove(key);
            _sets.Remove(key);
        }

        return Task.CompletedTask;
    }
}
