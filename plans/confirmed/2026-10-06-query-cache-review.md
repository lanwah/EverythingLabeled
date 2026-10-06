# 查询缓存（#10）讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）+ 设计 §1–§4 逐节确认 + grill Round 1–3  
已落盘同步：

- [查询缓存规格](../2026-10-06-query-cache.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#10 已确认规格）
- [项目看板](../../project.md)

---

## A. 主线澄清

### Q1 — #10 交付边界

**问题**：Memory / Redis 做到什么完整度？

| 选项 | 含义 |
|---|---|
| **A** | Memory **完整**实装（SizeLimit / MaxEntrySize / 键索引 + PostEviction / TTL / singleflight / 可观测）；Redis **完整**实装（可配启用，含前缀清理与淘汰策略） |
| **B** | Memory 完整；Redis 只做可编译薄封装 + 配置接线，前缀清理/容量策略标「可用但粗」，细节延后 |
| **C** | 本轮只做 Memory；`Provider=Redis` 启动时报错/拒绝，Redis 另开编号 |

**确认**：选推荐 **A**。

### Q2 — `SetAsync` 如何带 TTL

**问题**：现接口只有 `(key, value, ct)`，suggest/preview 需要绝对短 TTL，MaxEntrySize 在 Set 侧决策。

| 选项 | 含义 |
|---|---|
| **A** | 扩展为 `SetAsync(key, value, CacheEntryOptions? options, ct)`；`options` 含可选绝对过期；MaxEntrySize 由实现按 value 估算后 skip |
| **B** | 拆 `SetSuggest` / `SetPreview` / `SetQuery`，TTL 在各方法内读配置 |
| **C** | 接口不改；TTL 由实现根据 key 前缀推断 |

**确认**：选推荐 **A**。

### Q3 — singleflight 放哪一层

**问题**：#10 按 cache key 合并并发 miss 打库。

| 选项 | 含义 |
|---|---|
| **A** | 放进 `QueryCachePipeline.GetOrLoadAsync`；`ITagQueryCache` 只管存取 |
| **B** | 放进各 `ITagQueryCache` 实现内部 |
| **C** | 独立 `IQueryCacheCoordinator` 包装 cache + flight |

**确认**：选推荐 **A**。

### Q4 — Memory 条目 Size 怎么估

**问题**：`IMemoryCache` SizeLimit 需要每条 Size；估不准会过早逐出或撑爆。

| 选项 | 含义 |
|---|---|
| **A** | 粗估公式（key UTF-8 + 固定开销 + 字段粗估）；集中 `CacheEntrySizer`；偏保守 |
| **B** | Set 时 JSON 序列化量字节作 Size（Memory 仍存对象） |
| **C** | 固定 Size=1，SizeLimit 改成条目数（与已钉字节语义冲突） |

**确认**：选推荐 **A**。

### Q5 — Redis 的 `InvalidatePrefix` 怎么做

**问题**：Memory 用进程内键索引；Redis 跨进程，进程内索引不够。

| 选项 | 含义 |
|---|---|
| **A** | Redis 旁路索引：每个 version 一个 SET；Set 时 `SADD`；Invalidate 时 `SMEMBERS` + 批量 `DEL`；过期缺键忽略 |
| **B** | `SCAN MATCH v{n}:*` + 批量 DEL |
| **C** | Redis 只靠 TTL + `maxmemory`，不真前缀删 |

**确认**：选推荐 **A**。

### Q6 — Redis 客户端怎么接

**问题**：架构曾写可基于 `IDistributedCache`，但前缀删 + SET 索引对抽象不友好。

| 选项 | 含义 |
|---|---|
| **A** | Memory：`IMemoryCache`。Redis：直接 `StackExchange.Redis`。不经 `IDistributedCache` |
| **B** | 两边都走 `IDistributedCache`；前缀清理另开 Redis 连接做索引 |
| **C** | 只用 `IDistributedCache`，前缀清理退化为 SCAN 或放弃索引 |

**确认**：选推荐 **A**。配置新增 `Cache:RedisConnection`，不改已钉五键名。

### Q7 — Redis 容量上限怎么落实

**问题**：`Cache:SizeLimit` 是进程内字节顶；Redis 另有 `maxmemory`。

| 选项 | 含义 |
|---|---|
| **A** | SizeLimit **只约束 Memory**。Redis：运维设 `maxmemory` + `allkeys-lru`；客户端不再做第二套字节账 |
| **B** | Redis 客户端也维护近似字节账，超 SizeLimit 则拒绝 Set / 自删旧键 |
| **C** | 启用 Redis 时忽略 SizeLimit；只靠 TTL + 前缀删，不要求 maxmemory |

**确认**：选推荐 **A**。MaxEntrySize / TTL / 前缀索引两边都做。

### Q8 — Redis 连不上 / `Provider` 非法

**问题**：启动期和运行期失败语义不同。

| 选项 | 含义 |
|---|---|
| **A** | `Provider=Redis` 且连接失败 → **进程启动失败**。运行中断线 → Get/Set/Invalidate 失败只 Warning；读当 miss；写不翻车。未知 Provider → 启动失败 |
| **B** | Redis 连不上自动回退 Memory（静默） |
| **C** | Redis 失败则全程当 Null，进程仍启动 |

**确认**：选推荐 **A**。

### Q9 — Redis 存什么字节

**问题**：§2.3 Redis 存序列化字节；MaxEntrySize 用真实字节。

| 选项 | 含义 |
|---|---|
| **A** | `System.Text.Json`（UTF-8）；类型靠 Get 的泛型 `T`，不写 `$type`。超 MaxEntrySize 不 Set |
| **B** | MessagePack / 自定义二进制 |
| **C** | JSON + 类型头，支持多态 |

**确认**：选推荐 **A**。

### Q10 — `Cache:Provider` 要不要保留 `Null`

**问题**：#9 的 Null 给测试和冷路径用。

| 选项 | 含义 |
|---|---|
| **A** | 合法值：`Memory` \| `Redis` \| `Null`。`Null` 仅排障；默认仍 Memory |
| **B** | 配置只有 `Memory` \| `Redis`；Null 仅测试 DI |
| **C** | 砍掉 Null 类型，测试改用 Memory + 极小 SizeLimit |

**确认**：选推荐 **A**。

### Q11 — Redis 实现怎么测

**问题**：本机 CI 不一定有 Redis。

| 选项 | 含义 |
|---|---|
| **A** | Memory / pipeline / sizer：**默认单测**。Redis 真机：**可选**集成测（`REDIS_TEST_URL` 或本机 6379 才跑，否则 skip）。不强制 Testcontainers |
| **B** | CI 用 Testcontainers 必跑 Redis |
| **C** | Redis 实现不写自动化测，只测 Memory |

**确认**：选推荐 **A**。

### 实现路径

**问题**：双实现是否影响切换？是否抽缓存基类？

**确认**：切换只经 `ITagQueryCache` + `Cache:Provider`。Memory / Redis / Null **并列实现**，无基类。选推荐路径：**双实现 + 共享 Pipeline / 配置 / Sizer**；不抽 `IDistributedCache`、不抽跨后端抽象基类。

---

## B. 设计逐节确认

| 节 | 内容 | 确认 |
|---|---|---|
| 1 | 组件与接线（接口 `CacheEntryOptions`、DI 按 Provider 注册一个实现） | 可以 |
| 2 | 读路径与 singleflight（TTL 由调用方传入；合法空可 Set；Paged 快照） | 可以 |
| 3 | 失效、容量、Redis 键（`el:qc:`、索引 SET、SizeLimit 不约束 Redis） | 可以 |
| 4 | 错误、可观测性、测试、非目标 | 可以 |
| 落盘 | `plans/2026-10-06-query-cache.md` | 可以落盘 |

---

## C. Grill Round 1

### G1 — singleflight 遇上取消

**问题**：Singleton flight 同 key 共用 `load`。先到请求取消时，后到的会不会一起失败？`load` 抓着 Scoped `DbContext`。

| 选项 | 含义 |
|---|---|
| **A** | 领头 CT 不传入 load；或 flight 内 `CreateScope` 打库。请求 CT 只取消「等结果」 |
| **B** | 任一取消则整次 flight 取消，其它请求自己再 miss 打库 |
| **C** | 全部请求都取消才取消的 linked token |

**确认**：选推荐 **A**（Round 1）。**Round 2 G7 收束落地为「一律 await，不 CreateScope」**，覆盖本条的 Scope 实现细节。

### G2 — 默认 `IMemoryCache` 会不会和查询缓存抢 SizeLimit

**问题**：Web SDK 主机可能已有未设 SizeLimit 的 `IMemoryCache`。

| 选项 | 含义 |
|---|---|
| **A** | `MemoryTagQueryCache` **自建** `MemoryCache`，不占用主机默认实例 |
| **B** | 替换主机默认 `IMemoryCache` 并设 SizeLimit |
| **C** | 用命名/keyed cache |

**确认**：选推荐 **A**。

### G3 — 共享 Redis 时如何隔离

**问题**：两套不同 SQLite 共用 Redis 会互相喂脏缓存。仅写死 `el:qc:` 不够。

| 选项 | 含义 |
|---|---|
| **A** | 增加可配 `Cache:KeyPrefix`（默认 `el:qc:`）；多库必须不同前缀 |
| **B** | 前缀纳入 SQLite 路径哈希（自动隔离） |
| **C** | v1 不隔离，文档写「不要共享 Redis」 |

**确认**：选推荐 **A**。

### G4 — Redis `SET` + `SADD` 非原子 + 索引无 TTL

**问题**：suggest/preview 过期后 idx SET 成员会留下；崩溃可能「有值无索引」或「有索引无值」。

| 选项 | 含义 |
|---|---|
| **A** | Set 用 batch/transaction：`SET`+`SADD`；索引 SET 不设 TTL；Invalidate 缺键忽略 |
| **B** | 索引 SET 的 TTL = 该 version 下条目最大 TTL（对无 TTL 的 query 没用） |
| **C** | Get miss 时顺手 `SREM`（懒清理） |

**确认**：选推荐 **A+C**。

### G5 — 静态 `QueryCachePipeline` 怎么接到 Singleflight / 日志

**问题**：规格新增 Singleton 类型，Pipeline 仍是静态方法。

| 选项 | 含义 |
|---|---|
| **A** | Pipeline 改为实例（Singleton），构造注入 cache、singleflight、`ILogger`；TTL 仍由调用方传入 |
| **B** | 保持静态，签名加 `QueryCacheSingleflight` + `ILogger` |
| **C** | singleflight 藏进 `ITagQueryCache`（推翻主线 Q3） |

**确认**：选推荐 **A**。

### G6 — Redis `Connect` 卡住 vs 启动失败

**问题**：规格写连不上则进程失败，但 SE.Redis 默认会重试，启动可能挂很久。

| 选项 | 含义 |
|---|---|
| **A** | 显式短超时 + `AbortOnConnectFail=true`；超时即失败退出 |
| **B** | 用库默认（可能数十秒） |
| **C** | 后台连，先以 Null 顶上（削弱主线 Q8） |

**确认**：Round 1 选推荐 **A**（当时建议写死 5s）。**后由用户修订为可配，省略默认 5s** → 见 §F。

---

## D. Grill Round 2

### G7 — singleflight 与 Scoped `DbContext` 怎么落地

**问题**：现有 `load` 是 Query/Suggest 闭包。若领头请求先结束，Context 会被 dispose。

| 选项 | 含义 |
|---|---|
| **A** | 所有等待者（含已取消的领头）**都 await 完 flight 再结束请求**；等待时忽略请求 CT。不必 `CreateScope`，Pipeline 仍收 `Func<CT, Task<T>>` |
| **B** | 取消的请求马上返回；flight 在 `CreateScope()` 里打库；API 改为在新 Scope 解析服务 |
| **C** | 取消则整个 flight 失败（回到 G1-B） |

**确认**：选推荐 **A**。（覆盖 G1 的 Scope 落地。）

### G8 — `Cache:KeyPrefix` 作用范围

**问题**：UoW 仍发逻辑前缀 `v{n}:`。若 Memory 也改逻辑键，Invalidate 对不上。

| 选项 | 含义 |
|---|---|
| **A** | 只加在 **Redis 物理键**；Memory 逻辑键不变；默认 `el:qc:` |
| **B** | Memory/Redis 逻辑键都加前缀，UoW 也要读配置 |
| **C** | 不要配置键，写死 `el:qc:` |

**确认**：选推荐 **A**。

### G9 — 非法容量/TTL 配置

**问题**：`SizeLimit=0`、负数、`MaxEntrySize=0`、TTL 空或零。

| 选项 | 含义 |
|---|---|
| **A** | 启动时校验：SizeLimit>0、MaxEntrySize>0 且 ≤ SizeLimit、TTL>0；失败则进程起不来 |
| **B** | 非法则回落推荐默认并 Warning |
| **C** | 不校验，交给 `MemoryCache` 自己炸 |

**确认**：选推荐 **A**。Redis Provider 仍校验 MaxEntrySize/TTL；SizeLimit 若配了 ≤0 也失败。

### G10 — `InvalidatePrefix` 收到无法解析的字符串

**问题**：不是 `v{n}:`。

| 选项 | 含义 |
|---|---|
| **A** | Warning + no-op（不抛） |
| **B** | 当全表前缀扫描删除 |
| **C** | 抛异常（写路径会被 UoW 吃掉） |

**确认**：选推荐 **A**。

### G11 — 默认 Information 时 hit/miss 设 Debug 等于生产看不见

**问题**：规格又要求可观测、无 stats 端点。

| 选项 | 含义 |
|---|---|
| **A** | skip / Invalidate 失败：Warning；hit/miss **不逐条打**；进程内计数，周期性 Information 汇总 |
| **B** | 全部 Debug，认生产看不见 |
| **C** | 每条 hit/miss 都 Information |

**确认**：选推荐 **A**。节奏由 G14 钉死。

### G12 — Redis JSON 选项

**问题**：HTTP API 已有 STJ 习惯；缓存字节只服务端自用。

| 选项 | 含义 |
|---|---|
| **A** | 缓存专用 `JsonSerializerOptions`（`JsonSerializerDefaults.Web`），与 HTTP 解耦；不写 `$type` |
| **B** | 与 Minimal API 共用同一份 options |
| **C** | 用 `JsonSerializer.Default` |

**确认**：选推荐 **A**。

---

## E. Grill Round 3

### G13 — `load` 抛错时 flight 怎么办

**问题**：共用 Task 失败若仍留在字典，同 key 会一直中毒。

| 选项 | 含义 |
|---|---|
| **A** | `finally` 按 key 摘掉 in-flight；异常传给所有等待者；不 Set；下次 miss 重新 load |
| **B** | 短时间负缓存失败 |
| **C** | 摘掉但不把异常传给等待者，等待者各自再 load |

**确认**：选推荐 **A**。

### G14 — 计数汇总怎么刷

**问题**：G11 留了「每 N 次或每 1 分钟」。

| 选项 | 含义 |
|---|---|
| **A** | 距上次 Information **≥ 60s** 则打一条汇总并清窗口；不另开 HostedService；skip / Invalidate 失败仍即时 Warning |
| **B** | 每 100 次操作打一条 |
| **C** | 两者都要 |

**确认**：选推荐 **A**。

### G15 — `Cache:KeyPrefix` 合法值

**问题**：只影响 Redis 物理键。

| 选项 | 含义 |
|---|---|
| **A** | 非空、建议以 `:` 结尾（默认 `el:qc:`）；空或纯空白 → 启动失败。索引键 = `{KeyPrefix}idx:{n}` |
| **B** | 空则回落默认 |
| **C** | 允许空前缀 |

**确认**：选推荐 **A**。

### G16 — 实例 Pipeline 的方法签名

**问题**：改成 Singleton 之后是否还把 `ITagQueryCache` 当参数传入？

| 选项 | 含义 |
|---|---|
| **A** | Pipeline 构造注入 cache + singleflight + `ILogger`；`GetOrLoadAsync` **不再**收 cache。测试 `new QueryCachePipeline(fakeCache, …)` |
| **B** | 方法仍收 cache |
| **C** | 静态方法保留，再包一层实例 |

**确认**：选推荐 **A**。

---

## F. 修订

### RedisConnectTimeout（覆盖 G6「写死 5s」）

**问题**：Connect 超时要不要写死？

**确认**：改为 **可配置** `Cache:RedisConnectTimeout`（`"hh:mm:ss"`）；**省略则默认 5 秒**。`Provider=Redis` 且超时 ≤0 → 启动失败。`AbortOnConnectFail=true` 仍生效。
