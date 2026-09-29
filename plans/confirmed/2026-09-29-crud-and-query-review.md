# CRUD + 查询解析（#9）讨论确认记录

日期：2026-09-29  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）+ 设计 §1–§5 逐节确认 + grill Round 1–8  
已落盘同步：

- [CRUD + 查询解析规格](../2026-09-29-crud-and-query.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#9 已确认规格）
- [项目看板](../../project.md)

---

## A. 主线澄清

### Q1 — #9 交付范围与 #10 边界

**问题**：业务 CRUD / 查询与缓存如何切片？

| 选项 | 含义 |
|---|---|
| **A** | #9 实装 HTTP API 全量业务路由 + 查询解析/执行；`ITagQueryCache` 空实现（直通 DB）；#10 换 Memory/Redis |
| **B** | #9 同上但不出现缓存接口；#10 再改读路径接入 |
| **C** | #9 只做 CRUD + 打标/关系；`query`/`preview`/`suggest` 放到 #10 |
| **D** | #9+#10 合成一轮 |

**确认**：选推荐 **A**。

### Q2 — Service 内部分层

**问题**：业务代码放哪？

| 选项 | 含义 |
|---|---|
| **A** | 单项目；新增 `Services/` 用例层；`Http/*Endpoints` 薄 |
| **B** | 业务主要写在 Minimal API endpoint；少量 helper |
| **C** | 现在拆出 `EverythingLabeled.Core` |

**确认**：选推荐 **A**。

### Q3 — 查询冷路径查库方式

**问题**：集合运算如何落地？

| 选项 | 含义 |
|---|---|
| **A** | EF Core LINQ / 可翻译查询为主；难点允许局部 SQL / `FromSql` |
| **B** | 手写 SQL 优先；EF 只做实体 CRUD |
| **C** | 候选 ResourceId 拉内存再运算 |

**确认**：选推荐 **A**。

### Q4 — `data_version` 递增方式

**问题**：写路径如何统一 bump？幂等无变更是否 bump？

| 选项 | 含义 |
|---|---|
| **A** | 集中 `CommitAsync`（SaveChanges + Meta bump 同事务）；禁止业务路径手写 bump |
| **B** | 每个写方法自己 SaveChanges + 手动 bump |
| **C** | EF SaveChanges 拦截器自动 bump |
| **i** | 仅当本事务确有持久化变更才 bump |
| **ii** | 凡 HTTP 写成功（含幂等无变更）都 bump |

**确认**：选推荐 **A + i**。

### Q5 — `NameInitials` 实现

**问题**：简拼用哪套？

| 选项 | 含义 |
|---|---|
| **A** | NuGet 拼音库封装为 `INameInitialsGenerator`；用例表锁定 |
| **B** | 自研极简映射表 |
| **C** | #9 占位（非 ASCII 跳过）；简拼延后 |

**确认**：选推荐 **A**。

### Q6 — 测试策略

**问题**：#9 如何验收？

| 选项 | 含义 |
|---|---|
| **A** | 单元 + Service/SQLite + 关键 HTTP 集成 |
| **B** | 几乎全靠进程级 HTTP 集成 |
| **C** | 单元/SQLite 为主；HTTP 仅 smoke |

**确认**：选推荐 **A**。

### Q7 — 查询流水线模块拆分

**问题**：`Services/Query` 怎么切？

| 选项 | 含义 |
|---|---|
| **A** | `QueryParser` + `QueryExpander` + `QueryExecutor` + 独立 `NormalizeQuery` |
| **B** | 单一 `QueryEngine` 包办 |
| **C** | Parser 独立；展开与执行合成 Executor |

**确认**：选推荐 **A**。

## B. 设计节确认

| 节 | 内容 | 确认 |
|---|---|---|
| §1 | 范围与边界 / 非目标 / 验收口径 | ok |
| §2 | 目录与组件 / DI / 横切 | ok |
| §3 | 写路径与关系约束 | ok |
| §4 | 查询读路径与缓存桩 | ok |
| §5 | 错误映射、测试与验收 | ok |

---

## C. Grill Round 1

### Q1 — 实体 `Revision` 在 #9 是否递增？

**问题**：数据模型写明本地写时 `Revision +1`，#9 初稿未提。

| 选项 | 含义 |
|---|---|
| **A** | 凡更新该行（Tag / Resource / TagRelation）均 `Revision++`；创建初值另定 |
| **B** | #9 一律写 0 / 不碰；留给同步阶段 |
| **C** | 仅 Tag 递增 |

**确认**：选推荐 **A**。

### Q2 — `child` 单父 / 成环并发写怎么防？

**问题**：应用层校验下两请求可能双写不同父。

| 选项 | 含义 |
|---|---|
| **A** | 写关系（含 batch）事务 + 写前再读校验；冲突 → `409` |
| **B** | 接受极罕见竞态 |
| **C** | 全局写串行化（单 writer 队列） |

**确认**：选推荐 **A**。

### Q3 — 「有持久化变更才 bump」如何判定？

**问题**：幂等 / 同值 PATCH / 已对齐 reorder 是否 bump？

| 选项 | 含义 |
|---|---|
| **A** | Service 显式 `dirty`；仅真正插删改才标；同值/幂等无变更不 bump |
| **B** | 信任 `ChangeTracker.HasChanges()` |
| **C** | 凡写 API HTTP 200 都 bump（推翻 A+i） |

**确认**：选推荐 **A**。

### Q4 — 未知正词软失败要不要走 `ITagQueryCache`？

**问题**：软失败是否 TryGet/Set？

| 选项 | 含义 |
|---|---|
| **A** | 不读写缓存；只缓存解析成功后的执行结果 |
| **B** | 软失败也按键缓存空结果 |
| **C** | TryGet 挪到规范化后、解析前 |

**确认**：选推荐 **A**。

### Q5 — `suggest` 缓存键怎么定？

**问题**：契约查询键含 expand 维；suggest 只有 q+limit。

| 选项 | 含义 |
|---|---|
| **A** | `v{ver}:suggest:{normalizedPrefix}:{limit}`（prefix = 小写 trim 的 q） |
| **B** | 硬套 query 模板，expand 等占位 |
| **C** | #9 不定义 suggest 键；留给 #10 |

**确认**：选推荐 **A**。

### Q6 — Service → HTTP 错误传递形态？

**问题**：预期业务失败如何表达？

| 选项 | 含义 |
|---|---|
| **A** | 统一 `ServiceResult<T>`；不用异常表达预期业务失败 |
| **B** | typed exception + 过滤器收口 |
| **C** | Result 与异常混用 |

**确认**：选推荐 **A**。

### Q7 — `POST /v1/relations/batch` 成功 `data` 形状？

**问题**：HTTP API 留了两种，须选定。

| 选项 | 含义 |
|---|---|
| **A** | `{ "items": [...], "dataVersion": n }` |
| **B** | `data` = relations 数组 |

**确认**：选推荐 **A**。

---

## D. Grill Round 2

### Q8 — 新建实体 `Revision` 初值？

| 选项 | 含义 |
|---|---|
| **A** | 创建后为 `0`；每次更新该行 `++` |
| **B** | 创建即为 `1` |

**确认**：选推荐 **A**。

### Q9 — 幂等无变更写成功时是否带回 `dataVersion`？

| 选项 | 含义 |
|---|---|
| **A** | 带，值为当前 Meta（未 bump） |
| **B** | 无变更时省略 |
| **C** | 无变更也强制 bump |

**确认**：选推荐 **A**。

### Q10 — `ServiceResult` 如何表达未知正词软成功？

| 选项 | 含义 |
|---|---|
| **A** | `Ok` + 空列表 `Data` + 可选 `Message` → 信封 `200` + `message` |
| **B** | 单独 `SoftOk` / `Notice` 状态 |
| **C** | 当业务错误 code，endpoint 特判成 200 |

**确认**：选推荐 **A**。

### Q11 — `ITagQueryCache` 存什么？

| 选项 | 含义 |
|---|---|
| **A** | 业务 `data` DTO（items/分页等）；不缓存信封；不缓存软失败 |
| **B** | 完整 HTTP 信封 JSON |
| **C** | 原始 ResourceId 列表，命中后再 hydrate |

**确认**：选推荐 **A**。

### Q12 — `IUnitOfWork` 与请求生命周期？

| 选项 | 含义 |
|---|---|
| **A** | `DbContext` + `IUnitOfWork` 均为 Scoped |
| **B** | 每次 Commit 新建短生命周期上下文 |
| **C** | Singleton 上下文 |

**确认**：选推荐 **A**。

### Q13 — 性能边界是否写入 #9 硬验收？

**问题**：HTTP API §9（batch≤100、suggest 仅前缀、relations 必过滤等）。

| 选项 | 含义 |
|---|---|
| **A** | 写入完成定义；违规对应 400/422；至少抽样测试 |
| **B** | 实现时尽量遵守，不写 DoD |
| **C** | 另开 backlog，不堵 #9 |

**确认**：选推荐 **A**。

### Q14 — Null 缓存下键工厂要不要在 #9 测？

| 选项 | 含义 |
|---|---|
| **A** | 单测锁定 query/preview/suggest 键格式（含未展开占位）及 Null 行为 |
| **B** | 键测试留给 #10 |
| **C** | 只测 Null 恒未命中 |

**确认**：选推荐 **A**。

---

## E. Grill Round 3

### Q15 — 缓存键里的 `:` 与别名 `proj:foo`？

| 选项 | 含义 |
|---|---|
| **A** | 键为不透明相等串；禁止按 `:` split 解析；含 `:` 的 query 合法 |
| **B** | 对 query/prefix 做长度前缀或 Base64 |
| **C** | 改罕用分隔符并回写已确认契约 |

**确认**：选推荐 **A**。

### Q16 — 谁调用 `CommitAsync`？

| 选项 | 含义 |
|---|---|
| **A** | 公开写用例方法结尾 Commit 恰好一次；helper 不 Commit |
| **B** | Endpoint 显式 Commit |
| **C** | 请求结束中间件自动 Commit |

**确认**：选推荐 **A**。

### Q17 — `ServiceResult` 是否携带 HTTP 状态码？

| 选项 | 含义 |
|---|---|
| **A** | 只带机器码/message/details/data；中央映射表 `code → HTTP` |
| **B** | Result 内含 `int HttpStatus` |
| **C** | 每个 endpoint 自己 switch |

**确认**：选推荐 **A**。

### Q18 — API 是否全异步？

| 选项 | 含义 |
|---|---|
| **A** | 一律 `Task` + `CancellationToken` |
| **B** | 缓存同步、DB 异步 |
| **C** | 能 sync 就 sync |

**确认**：选推荐 **A**。

### Q19 — `preview` 的 `total`？

| 选项 | 含义 |
|---|---|
| **A** | #9 省略 `total`；`query` 必有 |
| **B** | preview 也返回 total |
| **C** | preview 固定 `total: 0` |

**确认**：选推荐 **A**。

### Q20 — #9 落地后的说明文档？

| 选项 | 含义 |
|---|---|
| **A** | 新增 `docs/crud-and-query.md` |
| **B** | 只更新 `docs/service-skeleton.md` |
| **C** | 不写 docs |

**确认**：选推荐 **A**。

### Q21 — 拼音库 NuGet 是否在规格钉死？

| 选项 | 含义 |
|---|---|
| **A** | 只钉接口 + 黄金用例表；实现时选库 |
| **B** | 规格钉死包名/版本 |
| **C** | 不用第三方，自研映射 |

**确认**：选推荐 **A**。

---

## F. Grill Round 4

### Q22 — 未知正词成功 `message` 语言？

| 选项 | 含义 |
|---|---|
| **A** | 固定英文稳定句式 |
| **B** | 固定中文 |
| **C** | 随 `Accept-Language`（v1 不做） |

**确认**：选推荐 **A**。

### Q23 — Service 层测试用什么库？

| 选项 | 含义 |
|---|---|
| **A** | SQLite（临时文件或可控连接串）；不用 EF InMemory |
| **B** | EF Core InMemory |
| **C** | 只跑进程级 HTTP |

**确认**：选推荐 **A**。

### Q24 — 仅打标/去标是否更新 Resource.`UpdatedAt` / `Revision`？

| 选项 | 含义 |
|---|---|
| **A** | 不碰 Resource 行；只改 ResourceTag +（有变更时）data_version |
| **B** | 每次绑定变更 touch Resource UpdatedAt + Revision++ |
| **C** | 只更新 UpdatedAt，不改 Revision |

**确认**：选推荐 **A**。

### Q25 — 删到最后一个别名的拒绝码？

| 选项 | 含义 |
|---|---|
| **A** | `422 validation_failed`（details 可含 `reason=last_alias`） |
| **B** | `409` |
| **C** | `400 bad_request` |

**确认**：选推荐 **A**。

### Q26 — grill 结论回写范围？

| 选项 | 含义 |
|---|---|
| **A** | 修订规格正文 + 本审核记录（含问题与候选）；看板下一步改为起草计划或按 §8 开工 |
| **B** | 只改 review，规格不动 |
| **C** | 另开新 plan 文件 |

**确认**：选推荐 **A**（且回写须含问题 + 候选答案，主线澄清同样展开）。

---

## G. Grill Round 5（缓存写后正确性）

背景：审核「增/删/改后 query/preview/suggest 能否经 `data_version` 键拿到准确数据」。机制结论成立；补钉并发 bump 与读路径 Set 竞态。

### Q27 — `CommitAsync` 如何 bump，避免并发丢更新？

**问题**：两写并发若都对早期加载的 Meta 做内存 `++` 再写回，可能把 version 写成同一值，导致库已变但键前缀未跟上 → 缓存脏命中。

| 选项 | 含义 |
|---|---|
| **A** | `CommitAsync` 内对 Meta 原子 `+1`（`ExecuteUpdate` 或等价 SQL）；不依赖请求早期跟踪实体 |
| **B** | 事务内 `Reload` Meta 再 `++`，并配并发令牌；冲突重试 |
| **C** | v1 本机单写为主，接受极少丢 bump；不特化 |

**确认**：选推荐 **A**。

### Q28 — 读路径 `Set` 前是否复核 `data_version`？

**问题**：先读 version 组键 → 查库 → Set；若其间发生写 bump，可能把旧快照写入**当前** version 键。

| 选项 | 含义 |
|---|---|
| **A** | `Set` 前再读 Meta；与组键 version 不同则 **不 Set**，仍返回本次查库结果（**不**强制按新 version 重查）；`suggest` 同规则 |
| **B** | 不复核；接受孤儿键 / 极罕见错填 |
| **C** | 整段读（version + 查库 + Set）包在同一只读事务 / 快照 |

**确认**：选推荐 **A**。

---

## H. Grill Round 6（缓存容量与物理清理 → #10 硬约束）

背景：逻辑失效靠 `data_version` 键已够「写后读准」；旧键与键碎片若不清理/不设硬顶，Memory/Redis **可能撑爆**。约束写入规格 §2.3，供 #10 实装。

### Q29 — Memory 提供方的容量上限？

**问题**：仅靠 version 逻辑失效时，旧键仍占内存。

| 选项 | 含义 |
|---|---|
| **A** | `MemoryCache` + **SizeLimit**（条目 Size 估算）+ 满则压缩/逐出；配置可调；推荐默认约 128 MiB 量级 |
| **B** | 仅靠 TTL，不设硬上限 |
| **C** | 无 SizeLimit、无 TTL；只靠写后清旧 version 前缀 |

**确认**：选推荐 **A**。

### Q30 — 旧 `v{旧}:*` 物理清理时机？

**问题**：version 前进后孤儿键何时从存储移除？

| 选项 | 含义 |
|---|---|
| **A** | dirty 写成功后**异步** `InvalidatePrefix("v{previous}:")`（与 bump 配合） |
| **B** | 不主动清前缀；只靠 SizeLimit/LRU/TTL 自然挤掉 |
| **C** | 写路径**同步** `InvalidateAll` |

**确认**：选推荐 **A**。

### Q31 — TTL 是否分层？

**问题**：suggest/preview 键多易碎；query 更吃命中率。

| 选项 | 含义 |
|---|---|
| **A** | `suggest` / `preview` 短 TTL（推荐默认 60s，可配）；`query` 默认不靠短 TTL，主要靠 version + SizeLimit |
| **B** | 三类同一 TTL |
| **C** | 全部不过期，只靠 version + 容量逐出 |

**确认**：选推荐 **A**。

---

## I. Grill Round 7（缓存专题：空结果 / 击穿 / 前缀删 / 单条上限 / 形态 / 错误 / TTL / 接线）

补充：用户确认 **`SizeLimit` 与 `MaxEntrySize` 均须可配置，禁止写死**（默认值仅为推荐）。

### Q32 — 合法空结果要不要 Set？

**问题**：解析成功、库侧 0 命中（非未知正词软失败）。

| 选项 | 含义 |
|---|---|
| **A** | 缓存合法空结果（与非空相同：TryGet/Set + version 复核） |
| **B** | 空结果不 Set |
| **C** | 空结果单独更短 TTL |

**确认**：选推荐 **A**。

### Q33 — 同一键并发 miss 是否 singleflight？

| 选项 | 含义 |
|---|---|
| **A** | #10 按 cache key singleflight；#9 Null 不需要 |
| **B** | v1 不做 |
| **C** | 全局查询信号量，不按键合并 |

**确认**：选推荐 **A**。

### Q34 — Memory 下 `InvalidatePrefix` 怎么做？

| 选项 | 含义 |
|---|---|
| **A** | 旁路「version → 键集合」索引；按索引删键 |
| **B** | Memory bump 时改异步 `InvalidateAll`；Redis 仍前缀删 |
| **C** | 不真前缀删；只靠 SizeLimit/TTL |

**确认**：选推荐 **A**。

### Q35 — 单条目过大？

| 选项 | 含义 |
|---|---|
| **A** | 超过可配 `MaxEntrySize` 则跳过 Set，仍返回查库结果（**必须可配置，禁止写死**；推荐默认 1–2 MiB） |
| **B** | 不设单条上限 |
| **C** | 超大则压缩后再 Set |

**确认**：选推荐 **A**（并强调与 `SizeLimit` 一样可配）。

### Q36 — Memory 存值形态？

| 选项 | 含义 |
|---|---|
| **A** | Memory：不可变/防御拷贝对象图；Redis：序列化字节 |
| **B** | Memory/Redis 统一只存字节 |
| **C** | Memory 存可变 DTO 引用，约定只读 |

**确认**：选推荐 **A**。

### Q37 — 非成功读路径能否 Set？

| 选项 | 含义 |
|---|---|
| **A** | 仅成功执行结果（含合法空列表）可 Set |
| **B** | 可缓存部分 4xx |
| **C** | 展开触顶 422 也缓存 |

**确认**：选推荐 **A**。

### Q38 — suggest/preview 短 TTL：绝对还是滑动？

| 选项 | 含义 |
|---|---|
| **A** | 绝对 TTL（写入起算；命中不续期） |
| **B** | 滑动过期 |
| **C** | suggest 滑动、preview 绝对 |

**确认**：选推荐 **A**。

### Q39 — 写成功异步清理由谁接线？

| 选项 | 含义 |
|---|---|
| **A** | #9 `CommitAsync` dirty bump 后已调异步 `InvalidatePrefix(previous)`（Null no-op） |
| **B** | #9 不调用；#10 装饰器再挂 |
| **C** | 仅 Redis 实现内自行处理 |

**确认**：选推荐 **A**。

---

## J. Grill Round 8（配置 / 索引逐出 / 拷贝手法 / 可观测性 / 清理失败）

### Q40 — 配置面与推荐默认？

**问题**：SizeLimit、MaxEntrySize、TTL 等如何暴露？

| 选项 | 含义 |
|---|---|
| **A** | `Cache:*`；推荐默认 SizeLimit≈128MiB、MaxEntrySize≈2MiB、Suggest/Preview TTL=60s；**全部可配**；键名 #10 定但须进 appsettings 示例 |
| **B** | 仅 SizeLimit + MaxEntrySize 可配；TTL 写死 60s |
| **C** | 全部仅代码常量 + 环境变量 |

**确认**：选推荐 **A**。

### Q41 — SizeLimit 逐出时旁路键索引如何同步？

| 选项 | 含义 |
|---|---|
| **A** | `PostEvictionCallback`（或等价）逐出/过期时从索引移除 |
| **B** | 不回调；Invalidate 忽略缺失键 + 定期重建索引 |
| **C** | 不用索引；bump 时 Memory `InvalidateAll` |

**确认**：选推荐 **A**。

### Q42 — Memory 防御拷贝手法？

| 选项 | 含义 |
|---|---|
| **A** | 缓存专用不可变/record DTO；Set 拷入，TryGet 返回拷贝或只读视图 |
| **B** | Set/Get 都 JSON 往返 |
| **C** | 深拷贝库 + 可变类型 |

**确认**：选推荐 **A**。

### Q43 — #10 可观测性？

| 选项 | 含义 |
|---|---|
| **A** | 日志或简易计数：hit/miss/skip(MaxEntrySize)/invalidate；不强制对外 stats 端点 |
| **B** | 暴露 meta 或 `/v1/cache/stats` |
| **C** | v1 不做 |

**确认**：选推荐 **A**。

### Q44 — 异步 InvalidatePrefix 失败？

| 选项 | 含义 |
|---|---|
| **A** | 警告日志；不失败写请求；SizeLimit/TTL 兜底 |
| **B** | 失败则回退同步 InvalidateAll |
| **C** | 失败则写请求 5xx |

**确认**：选推荐 **A**。

---

## 实现入口

**缓存专题 grill frontier 已空**（Round 5–8）。规格（含 §2.3）为实现与 #10 权威约束。下一动作：起草 #9 实现计划，或直接按规格 §8 顺序开工。
