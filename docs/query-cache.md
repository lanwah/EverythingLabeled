# 查询缓存（#10）

日期：2026-10-06  
状态：已实现（`feature/query-cache`）  
规格：[plans/2026-10-06-query-cache.md](../plans/2026-10-06-query-cache.md)

默认 `Cache:Provider=Memory`（进程内私有 `MemoryCache`）。也可设 `Redis` 或 `Null`。启动只注册一个 `ITagQueryCache`，不热切换。

## 配置 `Cache:*`

| 键 | 含义 |
|---|---|
| `Provider` | `Memory` / `Redis` / `Null`（大小写不敏感） |
| `SizeLimit` / `MaxEntrySize` | 只约束 Memory 容量；Redis 也校验这两项 |
| `SuggestTtl` / `PreviewTtl` | 绝对过期；query 无短 TTL |
| `RedisConnection` | 仅 Redis；缺则启动失败 |
| `RedisConnectTimeout` | 仅 Redis；默认 5s |
| `KeyPrefix` | 仅 Redis 物理键前缀；默认 `el:qc:` |

写路径仍是 `UnitOfWork` 异步 `InvalidatePrefixAsync("v{previous}:")`。

## Redis 运维

- 服务端自行设 `maxmemory` + `allkeys-lru`；本进程不设 Redis `maxmemory`。
- 多库或同 Redis 多实例须用不同 `KeyPrefix`。
- 连接：`AbortOnConnectFail=true`，超时为 `RedisConnectTimeout`。

## 测试

默认 CI **不**依赖 Redis / Docker。`RedisTagQueryCacheTests` 用假 `IRedisCacheDatabase`。

真机 opt-in：设 `REDIS_TEST_URL`（否则试 `127.0.0.1:6379`）。连不上时 `RedisTagQueryCacheIntegrationTests` **提前 return**，xunit 显示通过，**不能当作真机覆盖**。
