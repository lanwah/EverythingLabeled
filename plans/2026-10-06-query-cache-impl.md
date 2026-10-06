# #10 查询缓存 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 #9 已接线的 `ITagQueryCache` + Null 桩上，实装可配置的 Memory / Redis / Null 缓存、实例 `QueryCachePipeline`（singleflight + 可观测），默认进程内 Memory。

**Architecture:** 三个 `ITagQueryCache` 并列实现，无缓存基类；启动按 `Cache:Provider` 只注册一个。Pipeline / Singleflight / Metrics / Sizer 共享。写路径仍由 `UnitOfWork` 异步 `InvalidatePrefixAsync($"v{previous}:")`。Memory 用私有 `MemoryCache` + 旁路索引；Redis 用 `StackExchange.Redis`（物理键 `KeyPrefix` + version SET 索引），不经 `IDistributedCache`。

**Tech Stack:** .NET 8、ASP.NET shared framework（`MemoryCache`、Options）、`StackExchange.Redis` 2.8.58、xunit、Serilog 经 `ILogger<T>`。默认 CI **不**依赖 Redis / Docker。

**Spec:** [plans/2026-10-06-query-cache.md](./2026-10-06-query-cache.md)。硬约束：[CRUD §2.3](./2026-09-29-crud-and-query.md)。审核：[confirmed/2026-10-06-query-cache-review.md](./confirmed/2026-10-06-query-cache-review.md)（含 **实现计划 grill Q1–Q15**）。既有接线：[CRUD 实现计划](./2026-10-03-crud-and-query-impl.md)。

## Global Constraints

- TFM `net8.0`；`<Nullable>enable</Nullable>`；`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。
- **不**拆 Core；**不**抽跨后端缓存基类；**不**用 `IDistributedCache`。
- `Cache:*` **只绑定不改名**：`Provider`、`SizeLimit`、`MaxEntrySize`、`SuggestTtl`、`PreviewTtl`。#10 追加：`RedisConnection`、`RedisConnectTimeout`、`KeyPrefix`。**禁止**把 SizeLimit / MaxEntrySize / 默认 TTL / 5s 超时写死在业务分支里（默认值只出现在 `CacheOptions` 属性初值与 `appsettings.json`）。
- `SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)`；无 `InvalidateAll`。
- `options == null`：不设短 TTL（`query`）。`suggest` → `SuggestTtl`；`preview` → `PreviewTtl`。绝对过期，命中不续期。
- `QueryCachePipeline` 为 **Singleton 实例**；`GetOrLoadAsync` **不再**收 `ITagQueryCache`。测试：`new QueryCachePipeline(fakeCache, …)`。
- singleflight 在 Pipeline：同 key 共用 load；**所有等待者（含已取消）都 await 完 load**；等待与 load **忽略请求 CT**（`CancellationToken.None`）；**不** `CreateScope`；load 失败 `finally` 摘 flight、异常传给等待者、不 Set。
- Memory：`new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit })`，**不** `AddMemoryCache`。SizeLimit **只**约束 Memory。
- Redis：`AbortOnConnectFail=true`；`ConnectTimeout` = `RedisConnectTimeout` 毫秒（省略默认 5s）；`KeyPrefix` **只**加物理键；逻辑键 / UoW 前缀仍是 `v{n}:`。索引 `{KeyPrefix}idx:{n}`，成员为逻辑键。
- `InvalidatePrefix` 无法解析为恰好 `v{n}:`（`^v([0-9]+):$`）→ Warning + no-op，不抛。
- 运行期 Get/Set 异常：Warning；当 miss / 跳过 Set。Invalidate 异常仍由 UoW 捕获。超 MaxEntrySize：skip Set，Warning，仍返回查库结果。
- 可观测：无 stats 端点、无 HostedService。进程内 `hit`/`miss`/`skip_max_entry`/`invalidate`；距上次 Information **≥ 60s** 则汇总一条并清窗口。skip 与 Invalidate **解析失败**即时 Warning。
- JSON（仅 Redis）：缓存专用 `new JsonSerializerOptions(JsonSerializerDefaults.Web)`，无 `$type`。
- 启动失败：非法 Provider / Redis 无连接串或连不上 / SizeLimit≤0 / MaxEntrySize≤0 或 > SizeLimit / TTL≤0 / Redis 时 KeyPrefix 空或空白 / RedisConnectTimeout≤0。
- `Memory`/`Null` 不连 Redis，忽略连接串。
- 公开 API 一律 `Task` + `CancellationToken`。
- Service 测试 **不用** EF InMemory。commit subject 用仓库递增 `#序号`（当前 HEAD 为 `#40`，本计划任务从 `git log -1` +1 起），**不要**写成 backlog `#10`；不用 conventional commits。
- 本计划不覆盖 CLI、MCP、热切换 Provider、`InvalidateAll`、滑动过期、改键工厂格式、客户端设 Redis `maxmemory`。
- **实现计划 grill（必须遵守）：** Task 3 即 Bind `CacheOptions` 并注册 Metrics/Singleflight/Pipeline，缓存仍为 Null，且改 `AddEverythingLabeledServices(IConfiguration)` + `Program`；Task 5 只切换 Provider + Redis Connect。`StackExchange.Redis` **2.8.58**。Pipeline 只快照 `Paged<>`。Redis：`CreateBatch`；`RedisTagQueryCache` **单一**构造收 `IRedisCacheDatabase`；`RedisConnection.Connect` 只在 DI；`ConnectionMultiplexer` Singleton 由容器 Dispose。Sizer/`MemoryCache.Size` 溢出 `int` → 与超 MaxEntrySize 相同 skip。进程测只留 Null health + Redis 坏连接串非 0 退出。真 Redis 无服务器时 `return`（记 passed，文档写明空跑）。`InternalsVisibleTo` 测试程序集。启动仍先 DB migrate 再 Resolve 缓存。

---

## File structure

```text
src/EverythingLabeled.Service/
├── EverythingLabeled.Service.csproj          # + StackExchange.Redis 2.8.58
├── appsettings.json                          # + RedisConnectTimeout / KeyPrefix 注释性默认
├── Program.cs                                # AddEverythingLabeledServices(config)；启动期 Resolve 缓存
├── Hosting/ServiceRegistration.cs            # 按 Provider 注册一个 ITagQueryCache
└── Services/
    ├── Abstractions/ITagQueryCache.cs        # + CacheEntryOptions
    ├── Query/QueryService.cs                 # 注入 Pipeline + CacheOptions；TTL
    ├── Tags/SuggestService.cs                # 同上
    └── Caching/
        ├── CacheOptions.cs                   # 绑定 + Validate()
        ├── CacheVersionPrefix.cs             # 解析 v{n}:
        ├── CacheEntrySizer.cs
        ├── CacheJson.cs                      # Redis STJ
        ├── QueryCacheMetrics.cs
        ├── QueryCacheSingleflight.cs
        ├── QueryCachePipeline.cs             # 改为实例
        ├── QueryCacheKeyFactory.cs           # 不改格式
        ├── NullTagQueryCache.cs              # 新 Set 签名
        ├── MemoryTagQueryCache.cs
        ├── RedisTagQueryCache.cs             # 单一构造：IRedisCacheDatabase（不 Connect）
        ├── IRedisCacheDatabase.cs            # Redis 端口（测试可假）+ StackExchange 实现
        └── RedisConnection.cs                # Task 5：Connect(CacheOptions) → ConnectionMultiplexer
tests/EverythingLabeled.Service.Tests/
├── RecordingCache.cs                         # 新 Set 签名
├── SqliteServiceFixture.cs                   # Pipeline + Options 接线
├── ServiceProcessFixture.cs                  # 可选 ExtraEnvironment
├── QueryCachePipelineTests.cs
├── QueryServiceCacheHookTests.cs             # QueryService 新构造
├── CacheOptionsTests.cs
├── CacheVersionPrefixTests.cs
├── CacheEntrySizerTests.cs
├── QueryCacheMetricsTests.cs
├── QueryCacheSingleflightTests.cs
├── MemoryTagQueryCacheTests.cs
├── RedisTagQueryCacheTests.cs                # Fake IRedisCacheDatabase
├── RedisTagQueryCacheIntegrationTests.cs     # opt-in；无 Redis 则 return
├── CacheStartupTests.cs                      # 非法配置进程退出
└── ListLogger.cs                             # 测 Warning 文案
docs/query-cache.md
docs/README.md
docs/crud-and-query.md                        # §4 改为指向 #10
```

---

### Task 1: 契约、配置校验、Sizer、Metrics、Null 签名

**Files:**

- Create: `Services/Caching/CacheOptions.cs`, `CacheVersionPrefix.cs`, `CacheEntrySizer.cs`, `QueryCacheMetrics.cs`, `tests/.../CacheOptionsTests.cs`, `CacheVersionPrefixTests.cs`, `CacheEntrySizerTests.cs`, `QueryCacheMetricsTests.cs`, `ListLogger.cs`
- Modify: `ITagQueryCache.cs`, `NullTagQueryCache.cs`, `RecordingCache.cs`, `NullTagQueryCacheTests.cs`, `QueryCachePipeline.cs`（本任务只改 `SetAsync` 调用签名以编译；Pipeline 行为改 Task 3）, `appsettings.json`

**Interfaces:**

- Consumes: 无新外部类型
- Produces: 后续任务必须原样使用

```csharp
namespace EverythingLabeled.Service.Services.Abstractions;

public sealed record CacheEntryOptions(TimeSpan? AbsoluteExpirationRelativeToNow);

public interface ITagQueryCache
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class;
    Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken);
}
```

```csharp
namespace EverythingLabeled.Service.Services.Caching;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";
    public const long DefaultSizeLimit = 134217728;
    public const long DefaultMaxEntrySize = 2097152;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultRedisConnectTimeout = TimeSpan.FromSeconds(5);
    public const string DefaultKeyPrefix = "el:qc:";

    public string Provider { get; set; } = "Memory";
    public long SizeLimit { get; set; } = DefaultSizeLimit;
    public long MaxEntrySize { get; set; } = DefaultMaxEntrySize;
    public TimeSpan SuggestTtl { get; set; } = DefaultTtl;
    public TimeSpan PreviewTtl { get; set; } = DefaultTtl;
    public string? RedisConnection { get; set; }
    public TimeSpan RedisConnectTimeout { get; set; } = DefaultRedisConnectTimeout;
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;

    public string NormalizedProvider() => Provider.Trim();

    public bool IsMemory() => NormalizedProvider().Equals("Memory", StringComparison.OrdinalIgnoreCase);
    public bool IsRedis() => NormalizedProvider().Equals("Redis", StringComparison.OrdinalIgnoreCase);
    public bool IsNull() => NormalizedProvider().Equals("Null", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!IsMemory() && !IsRedis() && !IsNull())
            throw new InvalidOperationException($"Cache:Provider '{Provider}' is not Memory, Redis, or Null.");
        if (SizeLimit <= 0)
            throw new InvalidOperationException("Cache:SizeLimit must be > 0.");
        if (MaxEntrySize <= 0 || MaxEntrySize > SizeLimit)
            throw new InvalidOperationException("Cache:MaxEntrySize must be > 0 and <= SizeLimit.");
        if (SuggestTtl <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:SuggestTtl must be > 0.");
        if (PreviewTtl <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:PreviewTtl must be > 0.");
        if (!IsRedis())
            return;
        if (string.IsNullOrWhiteSpace(RedisConnection))
            throw new InvalidOperationException("Cache:RedisConnection is required when Provider is Redis.");
        if (RedisConnectTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache:RedisConnectTimeout must be > 0.");
        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new InvalidOperationException("Cache:KeyPrefix must be non-empty when Provider is Redis.");
    }
}
```

```csharp
public static class CacheVersionPrefix
{
    public static bool TryParse(string prefix, out int version)
    {
        version = 0;
        if (prefix.Length < 3 || prefix[0] != 'v' || prefix[^1] != ':')
            return false;
        return int.TryParse(
            prefix.AsSpan(1, prefix.Length - 2),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out version);
    }
}
```

`CacheEntrySizer.Estimate<T>(string key, T value)`（`T : class`），**偏高**：

- `EntryOverhead = 192`
- `size = EntryOverhead + UTF8(key) + EstimateValue(value)`
- `string`：`24 + UTF8`
- `QueryHitDto`：`16`（Guid）+ 各 string 字段
- `SuggestItemDto`：同上
- `Paged<QueryHitDto>` / `Paged<SuggestItemDto>`：`48` + `Offset/Limit/Total` 各 8 + items
- `QueryResultDto` / `PreviewResultDto`：同上（preview 无 Total 仍计 8 占位）
- 其它：`Math.Max(256, JsonSerializer.SerializeToUtf8Bytes(value).Length * 2)`（保守；禁止 `$type`）
- 空列表：仍 ≥ `EntryOverhead`（断言 `> 0`）
- 与 key UTF-8 比较：对同一 ASCII key，`Estimate(key, "") >= 192 + key.Length`

`QueryCacheMetrics`：

```csharp
public sealed class QueryCacheMetrics(ILogger<QueryCacheMetrics> logger)
{
    public const int FlushIntervalSeconds = 60;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(FlushIntervalSeconds);
    public void Hit();
    public void Miss();
    public void SkipMaxEntry();
    public void Invalidate();
}
```

- 四个 `long` 用 `Interlocked.Increment`
- `MaybeFlush`：`Stopwatch.GetTimestamp()`；若距上次成功 flush ≥ 60s，`CompareExchange` 赢者 `Exchange` 清零并 `LogInformation("Query cache stats hit={Hit} miss={Miss} skip_max_entry={Skip} invalidate={Invalidate}", …)`
- **不** HostedService；由 Hit/Miss/Skip/Invalidate 顺带触发

`NullTagQueryCache.SetAsync` 忽略 value/options，返回 `CompletedTask`。

`appsettings.json` 保持现有五键，追加（Memory 默认仍可不写连接串）：

```json
"RedisConnectTimeout": "00:00:05",
"KeyPrefix": "el:qc:"
```

不要在默认 appsettings 写 `RedisConnection`。

- [ ] **Step 1: 写失败测试**

`CacheVersionPrefixTests`：`v12:` → 12；`v0:` → 0；`v12:query:`、`12:`、`v:`、`vabc:`、空串 → false。

`CacheOptionsTests`：合法 Memory 默认 `Validate()` 不抛；`Provider=redis`（大小写）合法但缺连接串抛；`SizeLimit=0`、`MaxEntrySize=0`、`MaxEntrySize > SizeLimit`、`SuggestTtl=0`、Redis `KeyPrefix=" "`、`RedisConnectTimeout=0` 均抛。`Provider=Disk` 抛。Memory 即使设了 `RedisConnection` 也 Validate 通过。

`CacheEntrySizerTests`：空 `Paged<QueryHitDto>` Size `> 0`；含中文 `DisplayName` 的条目 ≥ UTF-8 字节；同一 value 更长 key 的 Estimate 更大。

`QueryCacheMetricsTests`：三次 Hit 后用 `ListLogger`；**不要**等 60s。测内部可测缝：给 `QueryCacheMetrics` 增加内部构造 `QueryCacheMetrics(ILogger, TimeProvider)` **仅当**需要。更简单：把 flush 间隔抽 `internal TimeSpan FlushInterval { get; init; } = 60s`，测试设为 `TimeSpan.Zero` 则每次 Hit 都打 Information。**生产默认 60s**。测试：`FlushInterval = TimeSpan.Zero`，Hit 一次后 logger 含 `hit=1`。

`NullTagQueryCacheTests`：改为 `SetAsync("k", value, options: null, ct)`，行为不变。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~CacheOptionsTests|FullyQualifiedName~CacheVersionPrefixTests|FullyQualifiedName~CacheEntrySizerTests|FullyQualifiedName~QueryCacheMetricsTests`

Expected: FAIL（类型不存在）

- [ ] **Step 3: 最小实现**

按上面签名实现。同步改 Pipeline / RecordingCache 的 `SetAsync` 四参数，**暂不**改 Pipeline 控制流（`options: null`）。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test EverythingLabeled.sln`

Expected: PASS（含既有 117+ 新测）

- [ ] **Step 5: Commit**

```bash
git add src/EverythingLabeled.Service tests/EverythingLabeled.Service.Tests
git commit -m "$(cat <<'EOF'
#41 查询缓存契约、配置校验与 Sizer

EOF
)"
```

（序号以当时 `git log -1` +1 为准，下同。）

---

### Task 2: `MemoryTagQueryCache`

**Files:**

- Create: `MemoryTagQueryCache.cs`, `MemoryTagQueryCacheTests.cs`
- Modify: 无 DI（Task 5 再挂）

**Interfaces:**

- Consumes: `CacheOptions`（SizeLimit / MaxEntrySize）、`CacheEntrySizer`、`CacheVersionPrefix`、`QueryCacheMetrics`、`ILogger<MemoryTagQueryCache>`
- Produces:

```csharp
public sealed class MemoryTagQueryCache : ITagQueryCache, IDisposable
{
    public MemoryTagQueryCache(
        CacheOptions options,
        QueryCacheMetrics metrics,
        ILogger<MemoryTagQueryCache> logger);

    // TryGet / Set / InvalidatePrefix — ITagQueryCache
    public void Dispose();
}
```

实现要点：

- 字段：`MemoryCache _cache`（`SizeLimit = options.SizeLimit`）；`ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _index`（外键为逻辑前缀 `v{n}:`）。
- `SetAsync`：`var size = CacheEntrySizer.Estimate(key, value)`；若 `size > MaxEntrySize` **或** `size > int.MaxValue` → `metrics.SkipMaxEntry()` + `LogWarning("Query cache skip_max_entry key={Key} size={Size} max={Max}", …)`，return。否则 `MemoryCacheEntryOptions`：`Size = (int)size`；`AbsoluteExpirationRelativeToNow = options?.AbsoluteExpirationRelativeToNow`；**不**设 SlidingExpiration。`PostEvictionCallback`：若 `EvictionReason != Replaced`，从对应 prefix 字典摘 key，空则摘外层。从 `key` 用 `CacheVersionPrefix.TryParse` 取 prefix：对逻辑键 `v12:query:…` **不能**整串当 prefix。抽取：键若以 `v{n}:` **开头**，索引挂在 `v{n}:` 下（`TryParse` 只接受恰好 `v{n}:`，故另提供 `TryGetVersionPrefix(string logicalKey, out string prefix)`：扫描第一个 `:` 前为 `v`+digits）。
- `TryGetVersionPrefix`：`v12:query:x` → prefix `v12:`；无法解析则 **仍 Set 进 MemoryCache 但不进索引**（Invalidate 清不到，靠 SizeLimit/TTL）。
- `TryGetAsync`：`_cache.TryGetValue`；命中返回同一引用（Pipeline 已快照）。
- `InvalidatePrefixAsync`：`TryParse` 失败 → `LogWarning("Query cache InvalidatePrefix ignored prefix={Prefix}", prefix)`，return。成功则取出内层 keys → 每个 `_cache.Remove` → 删外层索引项 → `metrics.Invalidate()`。
- `Dispose` → `_cache.Dispose()`。
- Get/Set 包 try/catch：Warning，Get 返回 null，Set no-op。

- [ ] **Step 1: 写失败测试**

```csharp
public sealed class MemoryTagQueryCacheTests
{
    private static MemoryTagQueryCache Create(CacheOptions? o = null)
    {
        o ??= new CacheOptions();
        o.Validate();
        return new MemoryTagQueryCache(
            o,
            new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
            NullLogger<MemoryTagQueryCache>.Instance);
    }

    [Fact]
    public async Task Set_then_Get_roundtrips_payload()
    {
        using var cache = Create();
        var page = new Paged<QueryHitDto>
        {
            Items = [new QueryHitDto(Guid.NewGuid(), "file:///a", "file", "a")],
            Offset = 0, Limit = 50, Total = 1
        };
        await cache.SetAsync("v1:query:a:none:0:0:50:0", page, null, CancellationToken.None);
        var got = await cache.TryGetAsync<Paged<QueryHitDto>>("v1:query:a:none:0:0:50:0", CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal(page.Items[0].Uri, got!.Items[0].Uri);
    }

    [Fact]
    public async Task Ttl_does_not_slide_on_hit() { /* Set 80ms TTL；立即 Get 命中；再等 >TTL Get miss；两次 Get 之间不要延长寿命 */ }

    [Fact]
    public async Task MaxEntrySize_skips_set()
    {
        var options = new CacheOptions { SizeLimit = 10_000, MaxEntrySize = 100 };
        using var cache = Create(options);
        var huge = new string('x', 5000);
        await cache.SetAsync("v1:query:h:none:0:0:50:0", huge, null, CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<string>("v1:query:h:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_only_matching_version()
    {
        using var cache = Create();
        await cache.SetAsync("v1:query:a:none:0:0:50:0", "a", null, CancellationToken.None);
        await cache.SetAsync("v2:query:a:none:0:0:50:0", "b", null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v1:", CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
        Assert.Equal("b", await cache.TryGetAsync<string>("v2:query:a:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_prefix_is_noop()
    {
        using var cache = Create();
        await cache.SetAsync("v1:query:a:none:0:0:50:0", "a", null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("not-a-prefix", CancellationToken.None);
        Assert.Equal("a", await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
    }

    [Fact]
    public async Task Eviction_removes_index_no_ghost()
    {
        var options = new CacheOptions { SizeLimit = 400, MaxEntrySize = 400 };
        using var cache = Create(options);
        for (var i = 0; i < 50; i++)
            await cache.SetAsync($"v1:query:{i}:none:0:0:50:0", new string('y', 80), null, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v1:", CancellationToken.None); // 不得抛；幽灵键 Remove 可忽略
        Assert.Null(await cache.TryGetAsync<string>("v1:query:0:none:0:0:50:0", CancellationToken.None));
    }
}
```

TTL 测试用 `CacheEntryOptions(TimeSpan.FromMilliseconds(80))`，`await Task.Delay(200)` 后 miss。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~MemoryTagQueryCacheTests`

Expected: FAIL

- [ ] **Step 3: 实现 MemoryTagQueryCache**

含 `TryGetVersionPrefix`（可放在 `CacheVersionPrefix`）：

```csharp
public static bool TryGetLogicalPrefix(string logicalKey, out string prefix)
{
    prefix = "";
    var colon = logicalKey.IndexOf(':');
    if (colon <= 1) return false;
    var candidate = logicalKey[..(colon + 1)];
    if (!TryParse(candidate, out _)) return false;
    prefix = candidate;
    return true;
}
```

并补 `CacheVersionPrefixTests`：`v12:query:x` → `v12:`。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test EverythingLabeled.sln`  
Expected: PASS

- [ ] **Step 5: Commit** — `#42 Memory 查询缓存与前缀索引`

---

### Task 3: Singleflight + 实例 Pipeline + Query/Suggest TTL

**Files:**

- Create: `QueryCacheSingleflight.cs`, `QueryCacheSingleflightTests.cs`
- Modify: `QueryCachePipeline.cs`, `QueryCachePipelineTests.cs`, `QueryService.cs`, `SuggestService.cs`, `SqliteServiceFixture.cs`, `QueryServiceCacheHookTests.cs`, `Hosting/ServiceRegistration.cs`, `Program.cs`

**Interfaces:**

- Consumes: Task 1 Metrics / CacheEntryOptions / CacheOptions；`ITagQueryCache`
- Produces:

```csharp
public sealed class QueryCacheSingleflight
{
    public Task<T> RunAsync<T>(string key, Func<Task<T>> load) where T : class;
}

public sealed class QueryCachePipeline(
    ITagQueryCache cache,
    QueryCacheSingleflight singleflight,
    QueryCacheMetrics metrics,
    ILogger<QueryCachePipeline> logger)
{
    public Task<T> GetOrLoadAsync<T>(
        string key,
        int versionAtKey,
        Func<CancellationToken, Task<int>> readDataVersion,
        Func<CancellationToken, Task<T>> load,
        CacheEntryOptions? options,
        CancellationToken cancellationToken) where T : class;
}
```

`RunAsync` 实现：

```csharp
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
```

`GetOrAdd` 的 `addValueFactory` 必须用 **同一** `load` 委托；`LazyThreadSafetyMode.ExecutionAndPublication` 为默认。失败时 Task  faulted，所有等待者观察到同一异常；finally 摘掉 **这个** lazy，下次 miss 可重试。

Pipeline 顺序：

1. `TryGet`（用调用方 CT）。命中 → `metrics.Hit()` return。Get 抛 → Warning，当 miss。
2. `metrics.Miss()`。
3. `singleflight.RunAsync(key, async () => { … })` — **内部不看请求 CT**。
4. 内部：`var loaded = await load(CancellationToken.None)`；成功则 `readDataVersion(None)`；仅 version 未变才 `SetAsync(key, Snapshot(loaded), options, None)`。Set 抛 → Warning，仍返回 loaded。load 抛 → 不 Set，异常冒泡。
5. `Snapshot`：**只**处理 `Paged<QueryHitDto>` / `Paged<SuggestItemDto>`（`Items.ToArray()` 成新 `Paged`）。其它类型原样引用（不快照 `QueryResultDto`/`PreviewResultDto`）。

QueryService：去掉 `ITagQueryCache` 参数；注入 `QueryCachePipeline` + `IOptions<CacheOptions>`。`op == "preview"` 时 `new CacheEntryOptions(cacheOptions.Value.PreviewTtl)`，否则 `null`。

SuggestService：注入 Pipeline + `IOptions<CacheOptions>`；`new CacheEntryOptions(cacheOptions.Value.SuggestTtl)`。

`SqliteServiceFixture.GetQueryService` / `GetSuggestService`：

```csharp
public QueryCachePipeline CreatePipeline() =>
    new(Cache, new QueryCacheSingleflight(),
        new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
        NullLogger<QueryCachePipeline>.Instance);

public SuggestService GetSuggestService() =>
    new(Db, CreatePipeline(), Options.Create(new CacheOptions()));
```

`QueryServiceCacheHookTests` 里手写 `new QueryService(...)` 同样改。

**本 Task 必须改 DI（否则 HTTP 进程测无法解析 QueryService）：**

```csharp
public static IServiceCollection AddEverythingLabeledServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<CacheOptions>()
        .Bind(configuration.GetSection(CacheOptions.SectionName));
    services.AddSingleton<QueryCacheMetrics>();
    services.AddSingleton<QueryCacheSingleflight>();
    services.AddSingleton<ITagQueryCache, NullTagQueryCache>();
    services.AddSingleton<QueryCachePipeline>();
    // 其余 Scoped 与现有相同
}
```

`Program.cs`：`builder.Services.AddEverythingLabeledServices(builder.Configuration);`  
本 Task **不要** `GetRequiredService<ITagQueryCache>()`（仍 Null，无需启动期 Connect）。

- [ ] **Step 1: 写失败测试**

`QueryCacheSingleflightTests`：

- 同 key 两个并行 `RunAsync`，`load` 里 `TaskCompletionSource` + 计数器：`loadCalls == 1`，两者结果相同。
- load 抛一次后，第二次 `RunAsync` 再 load（`loadCalls == 2`）。

`QueryCachePipelineTests` 改为 `new QueryCachePipeline(...)`：

- 保留 miss+version 不变 Set / version 变不 Set / hit 不 load。
- **同 key 只 load 一次**（两个并行 GetOrLoad，RecordingCache + 慢 load）。
- **等待忽略 CT**：领头传入 `cancelled` token，仍拿到 load 结果（load 用 TCS 在取消之后才完成）。
- **合法空列表仍 Set**：`Paged<QueryHitDto>` Items 空。
- **Snapshot**：load 返回 `Items = mutableList` 的 `Paged<QueryHitDto>`；Set 之后 `mutableList.Clear()`；断言 `cache.Store[key]` 的 Items **不是**同一 List 实例。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~QueryCachePipelineTests|FullyQualifiedName~QueryCacheSingleflightTests`

Expected: FAIL（旧静态签名 / 无 singleflight）

- [ ] **Step 3: 实现并改接线**

- [ ] **Step 4: 全量测试**

Run: `dotnet test EverythingLabeled.sln`  
Expected: PASS

- [ ] **Step 5: Commit** — `#43 查询缓存 Pipeline singleflight 与 TTL`

---

### Task 4: Redis 实现（假数据库单测）

**Files:**

- Create: `IRedisCacheDatabase.cs`, `RedisTagQueryCache.cs`, `CacheJson.cs`, `RedisTagQueryCacheTests.cs`
- Modify: `EverythingLabeled.Service.csproj` — PackageReference `StackExchange.Redis` **2.8.58**（始终引用；仅 Redis Provider 连接）；`<InternalsVisibleTo Include="EverythingLabeled.Service.Tests" />`

**Interfaces:**

- Consumes: `CacheOptions.KeyPrefix` / `MaxEntrySize` / TTL options；`CacheJson.Options`；`CacheVersionPrefix`；`QueryCacheMetrics`
- Produces:

```csharp
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

internal sealed class StackExchangeRedisDatabase(IDatabase db) : IRedisCacheDatabase
{
    // PipelineSetAndIndexAsync: batch StringSet (expiry) + SetAdd；然后 Execute
    // 物理 SET：ttl 有值则 StringSet(key, value, ttl)；否则无过期
}

public sealed class RedisTagQueryCache : ITagQueryCache
{
    public RedisTagQueryCache(
        CacheOptions options,
        IRedisCacheDatabase database,
        QueryCacheMetrics metrics,
        ILogger<RedisTagQueryCache> logger);
    // 无第二构造、无 Connect；mux 生命周期见 Task 5
}

internal static class CacheJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
```

`Connect` **不**放在本类型。Task 5 的 `RedisConnection.Connect` 负责 Parse + `AbortOnConnectFail` + `ConnectTimeout`。

键：

- 物理：`{KeyPrefix}{logicalKey}` 例 `el:qc:v12:query:…`
- 索引：`{KeyPrefix}idx:{n}` 例 `el:qc:idx:12`

行为：

- `Set`：JSON UTF-8；`bytes.Length > MaxEntrySize` → skip + Warning（**不用** Sizer）。`TryParse`/`TryGetLogicalPrefix` 失败 → 只 SET 不 SADD。成功则 pipeline SET+SADD。
- `Get`：物理键 miss → 若能解析 version 则 `SREM`；反序列化失败当 miss + Warning。
- `Invalidate`：parse 失败 Warning no-op；否则 `SMEMBERS` → `DEL` 每个物理键 + `DEL` 索引（缺键忽略）→ `metrics.Invalidate()`。
- Get/Set 异常：Warning；miss / 跳过 Set。

- [ ] **Step 1: 写 Fake + 测试**

`FakeRedisCacheDatabase`：记录操作顺序的 list（`SET phys`、`SADD idx member`、`GET`、`SREM`、`SMEMBERS`、`DEL`）。内存 Dictionary 模拟 KV 与 SET。

测试：

- Set 后 Get 往返 `Paged<QueryHitDto>`（JSON Web camelCase）。
- Set 的 op 顺序：同一 pipeline 内 SET 物理键 **先于或并列于** SADD（记录为连续两条，SET 在 SADD 前）。
- 物理键以 `KeyPrefix` 开头；SADD 成员为**逻辑键**。
- Get miss：记录 SREM 该逻辑键。
- Invalidate `v12:`：DEL `el:qc:v12:…` 与 `el:qc:idx:12`。
- 超 MaxEntrySize：无 SET。
- 非法 prefix：无 DEL。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~RedisTagQueryCacheTests`

- [ ] **Step 3: 加 NuGet 并实现**

- [ ] **Step 4: 全量测试**（无真 Redis）

Run: `dotnet test EverythingLabeled.sln`  
Expected: PASS

- [ ] **Step 5: Commit** — `#44 Redis 查询缓存与索引 SET`

---

### Task 5: DI、启动失败、文档、#9 回归、opt-in 真 Redis

**Files:**

- Create: `docs/query-cache.md`, `CacheStartupTests.cs`, `RedisTagQueryCacheIntegrationTests.cs`, `Services/Caching/RedisConnection.cs`
- Modify: `Hosting/ServiceRegistration.cs`（按 Provider 换实现；Redis 时注册 mux）、`Program.cs`（migrate **之后** eager Resolve 缓存）、`ServiceProcessFixture.cs`（`IReadOnlyDictionary<string,string>? ExtraEnvironment`）、`docs/crud-and-query.md` §4, `docs/README.md`

**Interfaces:**

- Consumes: Task 1–4 全部类型
- Produces: 进程启动行为

```csharp
public static class RedisConnection
{
    public static ConnectionMultiplexer Connect(CacheOptions options)
    {
        var cfg = ConfigurationOptions.Parse(options.RedisConnection!);
        cfg.AbortOnConnectFail = true;
        cfg.ConnectTimeout = (int)options.RedisConnectTimeout.TotalMilliseconds;
        return ConnectionMultiplexer.Connect(cfg);
    }
}
```

`AddEverythingLabeledServices`：在 Task 3 已有 Bind + Metrics + Singleflight + Pipeline 之上，**删除** `AddSingleton<ITagQueryCache, NullTagQueryCache>()`，改为工厂：

- `Validate()` 后按 Provider：Memory → `MemoryTagQueryCache`；Null → `NullTagQueryCache`；Redis → `new RedisTagQueryCache(options, new StackExchangeRedisDatabase(mux.GetDatabase()), metrics, logger)`。
- **仅** `Provider` 解析为 Redis 时 `AddSingleton(sp => RedisConnection.Connect(options))`（`ConnectionMultiplexer` 实现 `IDisposable`，进程退出由容器 Dispose）。Memory/Null **不**注册 mux、不 Connect。

`Program.cs`：Task 3 已传入 `builder.Configuration`。本 Task 在 DB `InitializeAsync` **之后**、Map 端点之前：

```csharp
_ = app.Services.GetRequiredService<ITagQueryCache>();
_ = app.Services.GetRequiredService<QueryCachePipeline>();
```

以便 Redis Connect / Validate 在接受请求前失败（`catch` 已有 `Log.Fatal` + return 1）。

`ServiceProcessFixture.StartAsync` / `RunExpectExitAsync` 增加可选 `IReadOnlyDictionary<string, string>? extraEnv`。

- [ ] **Step 1: 启动失败测试**

`CacheStartupTests`（**只有两条进程测**；`Provider=Disk` / `SizeLimit=0` 已由 Task 1 `CacheOptions.Validate` 覆盖，不再拉进程）：

- `Cache__Provider=Redis`、`Cache__RedisConnection=127.0.0.1:1`、`Cache__RedisConnectTimeout=00:00:01`、`Cache__KeyPrefix=el:qc:` → `RunExpectExitAsync` exit ≠ 0（连不上；超时约 1s）。
- `Cache__Provider=Null` + `WriteConfigForFreePort` + `StartAsync`：`GET /v1/health` 200。用完 `DisposeAsync`。

真 Redis opt-in：

```csharp
public sealed class RedisTagQueryCacheIntegrationTests
{
    private static bool TryConnect(out ConnectionMultiplexer? mux)
    {
        mux = null;
        var url = Environment.GetEnvironmentVariable("REDIS_TEST_URL");
        var cs = string.IsNullOrWhiteSpace(url) ? "127.0.0.1:6379" : url;
        try
        {
            var cfg = ConfigurationOptions.Parse(cs);
            cfg.AbortOnConnectFail = true;
            cfg.ConnectTimeout = 500;
            mux = ConnectionMultiplexer.Connect(cfg);
            mux.GetDatabase().Ping();
            return true;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Roundtrip_and_prefix_invalidate()
    {
        if (!TryConnect(out var mux) || mux is null)
            return; // 无 Redis：xunit 记 passed；文档写明本条为空跑，勿当真机覆盖
        try
        {
            var options = new CacheOptions
            {
                Provider = "Redis",
                RedisConnection = mux.Configuration,
                KeyPrefix = $"el:qc:test:{Guid.NewGuid():N}:",
                RedisConnectTimeout = TimeSpan.FromSeconds(5),
            };
            options.Validate();
            var cache = new RedisTagQueryCache(
                options,
                new StackExchangeRedisDatabase(mux.GetDatabase()),
                new QueryCacheMetrics(NullLogger<QueryCacheMetrics>.Instance) { FlushInterval = TimeSpan.FromHours(1) },
                NullLogger<RedisTagQueryCache>.Instance);
            // Set v1 key, Get hit, Invalidate v1:, Get miss
        }
        finally { mux.Dispose(); }
    }
}
```

无 Redis 时 **return 即通过**；不要加 Testcontainers。

- [ ] **Step 2: 跑测试确认失败**（此时 DI 仍是 Task 3 的 Null；默认 appsettings `Provider=Memory` 尚未接到实现）

- [ ] **Step 3: 实现 DI + 文档**

`docs/query-cache.md` 写清：默认 Memory；`Provider` 三值；Redis 运维须 `maxmemory` + `allkeys-lru`；多库须不同 `KeyPrefix`；`REDIS_TEST_URL`；**无 Redis 时集成测提前 return，xunit 显示通过，不能当作真机覆盖**。不要抄规格全文。

`docs/crud-and-query.md` §4：默认改为 Memory；Null 仅 `Provider=Null`；链到 `query-cache.md`。

`docs/README.md` 加一行。

- [ ] **Step 4: 全量测试**

Run: `dotnet test EverythingLabeled.sln`  
Expected: PASS。#9 业务测在默认 Memory 下仍过（进程测读 output 目录 appsettings `Provider=Memory`）。

手工（可选）：`dotnet run --project src/EverythingLabeled.Service`，suggest 两次、写标签后再 suggest，第二次不得读到旧 version。

- [ ] **Step 5: Commit** — `#45 查询缓存 DI、启动校验与说明`

---

## Self-review

**1. Spec coverage**

| 规格 | Task |
|---|---|
| Memory 完整 SizeLimit/索引/TTL/PostEviction | 2 |
| Redis SET+SADD、KeyPrefix、懒 SREM、JSON Web | 4 |
| Provider Null + 启动只注册一个 | 5 |
| `SetAsync` + `CacheEntryOptions` | 1 |
| Pipeline 实例 + 不再收 cache 参数 | 3 |
| singleflight 在 Pipeline；失败摘 flight | 3 |
| 等待者一律 await；忽略请求 CT；不 CreateScope | 3 |
| 私有 MemoryCache | 2 |
| CacheEntrySizer 偏保守 | 1 |
| SizeLimit 只约束 Memory | 2、5 文档 |
| Redis StackExchange；AbortOnConnectFail；可配超时默认 5s | 4–5 |
| 启动失败矩阵 | 1 Validate + 5 进程测 |
| 运行期 Get/Set miss；Invalidate Warning | 2–4；UoW 已有 |
| ≥60s Information 汇总；skip 即时 Warning | 1 Metrics + 2/4 skip |
| 默认 CI 无 Redis；真机 opt-in | 4 fake + 5 skip |
| Query 无短 TTL；suggest/preview 绝对 TTL | 3 |
| Paged 快照（仅 Paged，不含 QueryResultDto） | 3 |
| MaxEntrySize / Size>int.MaxValue skip 仍返回库结果 | 2/4 Set；3 Pipeline 仍 return loaded |
| Task 3 即注册 Pipeline（仍 Null）；Task 5 才换 Provider | 3、5 |
| Redis mux Singleton + `RedisConnection.Connect` | 5 |
| 进程测仅 Null health + Redis 连不上 | 5 |
| 非目标未列入任务 | — |

**2. Placeholder scan:** 无 TBD；测试含具体断言与命令。

**3. Type consistency:** `GetOrLoadAsync` 六参数（无 cache）与 `AddEverythingLabeledServices(IConfiguration)` 在 Task 3 起固定；`RedisTagQueryCache` 单一构造收 `IRedisCacheDatabase`；`RedisConnection.Connect` 仅 Task 5 DI；物理键 / idx 格式在 Task 4 固定。

---

## 验收对照（规格 §9）

- 默认 Memory；suggest/preview 可命中；写后 Invalidate 旧 `v{n}:`。
- SizeLimit / MaxEntrySize / TTL / RedisConnectTimeout / KeyPrefix 可配。
- `Provider=Null` 永不命中。
- `Provider=Redis` 连接可用时语义与 Memory 等价（opt-in 测）。
- #9 全量测在默认 Memory 下通过。
