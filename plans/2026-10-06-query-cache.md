# EverythingLabeled 查询缓存规格（#10 `ITagQueryCache`）

日期：2026-10-06  
状态：已确认（v1 基线；含 grill Round 1–3）  
审核记录：[查询缓存规格审核确认](./confirmed/2026-10-06-query-cache-review.md)  
关联：

- [CRUD §2.3 硬约束](./2026-09-29-crud-and-query.md)（不可削弱）
- [本地守护进程架构 §5](./2026-09-22-local-daemon-architecture.md)
- [配置与安装](./2026-09-27-config-and-install.md)（`appsettings.json` 非密钥项）
- [路线图 backlog #10](./2026-09-23-roadmap-backlog.md)

## 1. 设计摘要

- #9 已接线 `ITagQueryCache` + `NullTagQueryCache` + `QueryCachePipeline` + `QueryCacheKeyFactory`；写路径 `CommitAsync` 已异步 `InvalidatePrefixAsync($"v{previous}:")`。
- #10 交付 **Memory 与 Redis 完整实装**（及配置可达的 Null）；默认 `Cache:Provider=Memory`，不强制安装 Redis。
- 切换只经接口 + DI：`MemoryTagQueryCache` / `RedisTagQueryCache` / `NullTagQueryCache` **并列实现** `ITagQueryCache`，无缓存基类。
- 启动时只注册 **一个** 实现；**不**做运行中热切换。
- Redis 用 `StackExchange.Redis`，**不**经 `IDistributedCache`。
- 硬约束沿用 CRUD §2.3。grill 增补见 §2 与 [审核记录](./confirmed/2026-10-06-query-cache-review.md)。

## 2. 已钉决策

**讨论 Q1–Q11** 见设计对话；**grill G1–G16** 见审核记录。实现时不可削弱的要点：

| 项 | 结论 |
|---|---|
| 交付 | Memory **与** Redis 均完整；`Provider` 含 `Null` |
| 接口 | `SetAsync(..., CacheEntryOptions? options, ct)`；无 `InvalidateAll` |
| singleflight | 在 **实例** `QueryCachePipeline`；`load` 失败则摘 flight、异常传给等待者、不 Set |
| Scope | 等待者（含已取消请求）**await 完 load**；等待忽略请求 CT；继续用调用方闭包 Scoped 服务 |
| Memory | **私有** `MemoryCache`（不占用主机默认 `IMemoryCache`）；`CacheEntrySizer` 偏保守 |
| Redis 客户端 | `StackExchange.Redis`；`AbortOnConnectFail=true`；连接超时 **可配** `RedisConnectTimeout`，缺省 **5s** |
| Redis 容量 | SizeLimit 只约束 Memory；Redis 靠服务端 `maxmemory` + `allkeys-lru` |
| Redis 键 | `KeyPrefix` **只**加物理键（默认 `el:qc:`）；逻辑键与 Invalidate 仍是 `v{n}:` |
| Redis 索引 | `SET`+`SADD` 流水线；Get miss 懒 `SREM`；不做 keyspace notify |
| JSON | 缓存专用 STJ（`JsonSerializerDefaults.Web`），无 `$type` |
| 启动失败 | 非法 Provider / Redis 无连接串或连不上 / 非法容量·TTL·KeyPrefix·超时 |
| 运行失败 | Get/Set 当 miss；Invalidate 只 Warning |
| 可观测 | 无 stats 端点；≥60s Information 汇总 hit/miss/skip/invalidate；skip 与 Invalidate 失败即时 Warning |
| 测试 | 默认不依赖 Redis；真机 opt-in |

路径：双实现 + 共享 Pipeline / 配置 / Sizer；不抽跨后端基类。

## 3. 组件与接线

读路径：`QueryService` / `SuggestService` → **实例** `QueryCachePipeline` → `ITagQueryCache`。  
写路径不变：`UnitOfWork` 异步 `InvalidatePrefixAsync`。

### 3.1 接口

```csharp
public sealed record CacheEntryOptions(TimeSpan? AbsoluteExpirationRelativeToNow);

public interface ITagQueryCache
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        where T : class;
    Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken);
}
```

- `options == null`：不设短 TTL（`query`）。
- `suggest` → `SuggestTtl`；`preview` → `PreviewTtl`。绝对过期，命中不续期。
- `NullTagQueryCache` / `RecordingCache` 同步新签名。
- `InvalidatePrefix` 无法解析为 `v{n}:` → Warning + no-op，不抛。

### 3.2 类型（`Services/Caching/`）

| 类型 | 生命周期 | 职责 |
|---|---|---|
| `CacheOptions` | Options | 绑定 `Cache:*` |
| `QueryCachePipeline` | Singleton | TryGet → singleflight load → version 复核 → Set；注入 cache / flight / logger |
| `QueryCacheSingleflight` | Singleton | 按 key 合并 in-flight；结束或失败后摘掉 |
| `MemoryTagQueryCache` | Singleton | 私有 `MemoryCache` + 进程内前缀索引 + PostEviction |
| `RedisTagQueryCache` | Singleton | 物理键 / 索引 SET / JSON |
| `NullTagQueryCache` | Singleton | 直通 |
| `CacheEntrySizer` | 无状态 | Memory Size 粗估 |

`QueryCachePipeline.GetOrLoadAsync` **不再**接收 `ITagQueryCache` 参数。测试：`new QueryCachePipeline(fakeCache, …)`。

### 3.3 配置 `Cache:*`

#9 已钉五键，#10 **只绑定不改名**，并追加：

| 键 | 语义 | 推荐默认 |
|---|---|---|
| `Provider` | `Memory` \| `Redis` \| `Null`（大小写不敏感） | `Memory` |
| `SizeLimit` | Memory 字节顶；禁止写死在业务代码 | `134217728` |
| `MaxEntrySize` | 超则跳过 Set | `2097152` |
| `SuggestTtl` / `PreviewTtl` | `"hh:mm:ss"` | `00:01:00` |
| `RedisConnection` | 仅 Redis | 无（缺则 Redis 启动失败） |
| `RedisConnectTimeout` | 仅 Redis；`"hh:mm:ss"` | **省略则 5s**（`00:00:05`） |
| `KeyPrefix` | 仅 Redis 物理键；须非空，建议以 `:` 结尾 | `el:qc:` |

启动校验（失败则进程起不来）：

- `Provider` 必须是三者之一。
- `SizeLimit > 0`；`MaxEntrySize > 0` 且 **≤ SizeLimit**（Redis 也校验这两项，避免「以为有顶」）。
- `SuggestTtl` / `PreviewTtl` > 0。
- `Provider=Redis`：`RedisConnection` 非空；`RedisConnectTimeout > 0`（缺省 5s）；`KeyPrefix` 非空且非纯空白。
- `Memory` / `Null` 不连 Redis，忽略连接串。

`appsettings.json` 示例保持默认 Memory；可不写 `RedisConnection`。

### 3.4 DI

- `Memory`：`MemoryTagQueryCache` **内部** `new MemoryCache(new MemoryCacheOptions { SizeLimit = … })`，**不** `AddMemoryCache` 去改主机默认实例。
- `Redis`：启动 `ConnectionMultiplexer.Connect`（`AbortOnConnectFail=true`，`ConnectTimeout` = 配置毫秒）；失败 → 进程失败。进程退出时 Dispose multiplexer。
- `Null`：现实现。
- 始终只注册一个 `ITagQueryCache`。`QueryCachePipeline` / `QueryCacheSingleflight` 始终注册。

NuGet：`StackExchange.Redis`（可始终引用，仅 Redis Provider 连接）。`MemoryCache` 来自 ASP.NET 共享框架，不另占主机缓存。

## 4. 读路径与 singleflight

顺序：

1. `TryGet` → 命中返回（不续期）；记 hit。
2. miss → `QueryCacheSingleflight.Run(key, load)`。同 key 共用一个 Task。
3. **所有等待者都 await 到结束**（含请求已取消者）；等待时忽略请求 CT。`load` 仍用调用方传入的 `Func`（Scoped 活到 await 结束）。
4. `load` 成功后读 `data_version`；仅 version 未变才 `Set(..., options)`。
5. `load` **抛错**：`finally` 摘 flight；异常传给所有等待者；不 Set；下次 miss 可重试。
6. 成功完成后摘 flight，防泄漏。
7. 无论是否 Set，成功结果返回本次请求。Set 运行期失败：Warning，仍返回库结果。

TTL 由 Suggest / Query 传入 options。解析 / 展开仍在进缓存之前。仅成功结果（含合法空列表）可 Set。

**Memory 存值：** 现有 record DTO。`Paged<T>`：Set 时快照 `Items` 为 `IReadOnlyList`。  
**Redis：** 专用 STJ `JsonSerializerDefaults.Web` UTF-8；按 `T` 反序列化；失败当 miss。MaxEntrySize 看序列化字节长度。

## 5. 失效、容量、键

逻辑失效：`v{data_version}:`。物理：`InvalidatePrefix("v{n}:")`。

### 5.1 Memory

- 条目 `Size` = `CacheEntrySizer`（偏高）。超 `MaxEntrySize` → skip Set。
- 索引：`ConcurrentDictionary<prefix, ConcurrentDictionary<key, byte>>`。`PostEvictionCallback` 同步摘索引。
- Invalidate：列出 key → `Remove` → 删索引。

### 5.2 Redis

- 逻辑键与 Memory 相同。
- 物理键：`{KeyPrefix}{logicalKey}`（默认 `el:qc:v{n}:query:…`）。
- 索引：`{KeyPrefix}idx:{n}`（例 `el:qc:idx:12`）为 SET，成员为**逻辑键**。
- Set：`CreateBatch`/`ITransaction` 流水线 `SET`（有 TTL 则 EXPIRE）+ `SADD`。
- Get 键不存在：当 miss，并 `SREM` 该逻辑键（懒清理）。
- Invalidate：`SMEMBERS` → `DEL` 物理键 → `DEL` 索引；缺键忽略。
- SizeLimit 不约束 Redis。文档：运维设 `maxmemory` + `allkeys-lru`。

## 6. 错误与可观测性

| 时机 | 行为 |
|---|---|
| 未知 Provider / 非法配置 | 启动失败 |
| Redis Connect 超时或失败 | 启动失败 |
| 运行期 Get/Set 异常 | Warning；miss / 跳过 Set |
| Invalidate 异常 | UoW 已捕获 |
| 超 MaxEntrySize | skip，不抛 |

进程内计数 `hit` / `miss` / `skip_max_entry` / `invalidate`。热路径：距上次汇总 **≥ 60s** 则 Information 一条并清窗口。**不**另开 HostedService。skip 与 Invalidate 失败：即时 Warning。无 `/v1/cache/stats`。

## 7. 测试

默认 CI 不依赖 Redis / Docker。

- Memory：Set/Get、TTL 不续期、逐出后无幽灵索引、MaxEntrySize skip、Invalidate 只清旧 version。
- Pipeline：同 key 只 load 一次；version 变不 Set；load 失败可重试；等待忽略 CT 仍拿到结果。
- Sizer：粗估 ≥ UTF-8；空列表 Size > 0。
- Redis 假 `IDatabase`：SET/SADD/DEL/SREM 顺序；含 KeyPrefix。
- 真 Redis：`REDIS_TEST_URL` 或本机 6379，否则 skip。
- 启动校验：非法 SizeLimit / 空 KeyPrefix（Redis）失败（可用 `WebApplicationFactory` 或纯 Options 校验测）。

## 8. 非目标

运行中热切换 Provider、多实例共享 Memory、HTTP MCP、stats API、`InvalidateAll`、滑动过期、改查询语言或键工厂格式、客户端代设 Redis `maxmemory`、flight 内 `CreateScope` 打库。

## 9. 验收

- 默认 Memory；suggest/preview 可命中；写后不读到旧 version。
- SizeLimit / MaxEntrySize / TTL / RedisConnectTimeout / KeyPrefix 可配；超 MaxEntrySize 仍返回查库结果。
- `Provider=Null` 永不命中。
- `Provider=Redis` 连接串可用时语义等价。
- #9 业务测在默认 Memory 下仍通过。
