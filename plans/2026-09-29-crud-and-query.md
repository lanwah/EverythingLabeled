# EverythingLabeled CRUD + 查询解析（#9）

日期：2026-09-29  
状态：已确认（v1 实现基线；主线澄清 + 设计 §1–§5 + grill Round 1–8 + 实现计划 grill 2026-10-03）  
审核记录：[CRUD + 查询解析讨论确认](./confirmed/2026-09-29-crud-and-query-review.md)  
关联：

- [Service 骨架](./2026-09-27-service-skeleton.md)（#8 已实现）
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)（§5 查询缓存）
- [路线图 backlog #9](./2026-09-23-roadmap-backlog.md)

契约细节（路由 JSON、词法、闸门、错误码）以关联已确认文档为准；本文只定 **#9 实现边界、分层与验收**，不重复抄全文。  
决策全文（问题 + 候选 + 确认）见[审核记录](./confirmed/2026-09-29-crud-and-query-review.md)；下文 §9–§10 为摘要表。

## 1. 设计摘要

- **范围**：在 #8 骨架上实装 [HTTP API](./2026-09-26-http-api.md) **全部业务路由** + [查询语言](./2026-09-26-query-language.md) 的 `query` / `preview`，以及 `suggest`（别名 + `NameInitials` 前缀）。
- **与 #10**：读路径经 `ITagQueryCache`；#9 提供 **Null 实现**（直通 DB）。Memory / Redis 实装 → #10，但须遵守下方 **#10 硬约束**（容量 / 旧前缀清理 / TTL）。
- **工程**：保持单项目 `EverythingLabeled.Service`；新增 `Services/` 用例层；`Http/*Endpoints` 保持薄；**不**预建 `EverythingLabeled.Core`。
- **数据访问**：写用例集中 `IUnitOfWork.CommitAsync()`（显式 dirty 才 **原子** bump）；查询冷路径以 **EF Core LINQ** 为主，难点允许局部 SQL / `FromSql`；读缓存 `Set` 前复核 `data_version`。
- **简拼**：`INameInitialsGenerator` + 黄金用例表；NuGet 拼音库实现时选定（规格不钉包名）。
- **落地文档**：实现完成后新增 `docs/crud-and-query.md`。

## 2. 范围与非目标

### 2.1 进 #9

| 域 | 内容 |
|---|---|
| Tags | 列表 / 详情 / by-alias / CRUD / aliases / neighbors |
| Resources | 列表 / 详情 / by-uri / CRUD；URI 规范化与 Kind 推断（规则对齐数据模型） |
| 打标 | `POST /v1/resources/tags`（幂等 add/remove；隐式建资源；`createMissing` 建标） |
| Relations | 边 CRUD、`reorder`、`batch`、约束（单父 / 成环 / 自环） |
| Query | `GET /v1/query`、`GET /v1/query/preview`（含 `expand=child` 闸门） |
| Suggest | `GET /v1/tags/suggest` |
| Meta | `GET /v1/meta`；写路径 `data_version` |
| 缓存桩 | `ITagQueryCache` + `NullTagQueryCache` 已接线；键工厂可测 |
| 性能边界 | [HTTP API §9](./2026-09-26-http-api.md) 硬约束（batch 上限、前缀检索、relations 必过滤等） |

### 2.2 非目标

- Memory / Redis 缓存**实装** → #10（逻辑失效仍靠 `data_version` 键；#9 只 Null + 接口）
- CLI / `EverythingLabeled.Client` / MCP / 浏览器扩展 / Explorer → #11+
- 多机同步、文件跟标、全盘索引、HTTP MCP（已延后）
- 拆出独立 Core 程序集
- OpenAPI 产物（可选，不堵 #9）
- 按 `Accept-Language` 切换查询 notice 文案

### 2.3 对 #10 的硬约束（grill Round 6–8；实现时不可削弱）

逻辑失效（写后读准）已由 `v{data_version}:…` 保证；下列约束防止**物理**撑爆并钉读路径语义：

| 项 | 约束 |
|---|---|
| 配置面 | 节 `Cache:*`。**键名已钉死**（#9 写入 appsettings 示例，#10 只绑定不改名）：`Provider`、`SizeLimit`、`MaxEntrySize`、`SuggestTtl`、`PreviewTtl`。SizeLimit/MaxEntrySize 为字节整数；TTL 为 `"00:01:00"` 形式。**禁止把 SizeLimit / MaxEntrySize 写死在业务代码里**；推荐默认：SizeLimit=**134217728**（128 MiB）、MaxEntrySize=**2097152**（2 MiB）、Suggest/Preview TTL=**60s**（均可覆盖） |
| Memory 容量 | 必须 SizeLimit + 条目 Size 估算；满则压缩/逐出 |
| 单条上限 | 超过 MaxEntrySize → **跳过 Set**，仍返回查库结果 |
| 旧前缀清理 | dirty bump 后异步 `InvalidatePrefix`（旁路 version→键索引）；**不**默认同步 `InvalidateAll`。索引须在 **PostEvictionCallback**（或等价）中随逐出/过期同步移除，避免幽灵键 |
| 清理失败 | 只打**警告日志**；**不**让写请求失败；靠 SizeLimit/TTL 兜底 |
| TTL | suggest/preview：**绝对**短 TTL（可配）；命中不续期。query：默认不靠短 TTL |
| 合并打库 | #10：按 cache key **singleflight**；#9 Null 不需要 |
| 存值形态 | Memory：缓存专用**不可变/record DTO**（Set 拷入；TryGet 返回拷贝或只读视图）；Redis：序列化字节 |
| 何可 Set | **仅**成功执行结果（含合法空列表）；软失败 / 4xx / 5xx / 展开触顶等不 Set |
| 可观测性 | #10：至少日志或简易计数 hit / miss / skip(MaxEntrySize) / invalidate；**不**强制对外 stats 端点 |
| 接线 | #9：`CommitAsync` dirty bump 后已调异步 `InvalidatePrefix(previous)`（Null no-op） |
| Redis | 若启用：同样需要淘汰 / TTL / 前缀清理，不得无限增长（细节 #10） |

## 3. 目录与组件
```text
src/EverythingLabeled.Service/
├── Services/
│   ├── Abstractions/          # IUnitOfWork, ITagQueryCache, INameInitialsGenerator, ServiceResult, …
│   ├── Tags/
│   ├── Resources/            # 含 UriNormalizer（#9 仅 Service 内；日后 Client 再对齐/抽取）
│   ├── Relations/
│   ├── Query/                # QueryParser, QueryExpander, QueryExecutor, NormalizeQuery, 键工厂
│   ├── Suggest/
│   └── Caching/              # NullTagQueryCache、QueryCacheKeyFactory、QueryCachePipeline（#9）
├── Http/                     # *Endpoints + code→HTTP 中央映射；绑定 → Service → ApiEnvelope
├── Data/                     # 已有
├── Auth/ / Hosting/ / Logging/
```

**依赖方向**：`Http` → `Services` → `Data`。查询内部：Parser → Expander → Executor；`NormalizeQuery` 为独立纯函数（解析 TagId **之前**）。

**DI**：`DbContext` 与 `IUnitOfWork` 均为 **Scoped**；`ITagQueryCache` → Null；其余 Service / 生成器在 `Hosting` 注册。

**异步**：公开用例与缓存 `TryGet`/`Set`/`CommitAsync` 一律 `Task` + `CancellationToken`。

**常量**：`QueryExpansionLimits` 集中定义，注释对齐[查询语言 §3.1](./2026-09-26-query-language.md)。

## 4. 写路径

### 4.1 提交与 `data_version`

- **公开写用例方法**结尾调用 `CommitAsync` **恰好一次**；内部 helper 只改实体 / 标 dirty，不 Commit。
- `CommitAsync`：同事务 `SaveChanges` +（**显式 dirty** 时）对 `Meta.data_version` 做**原子 +1**（EF `ExecuteUpdate` 或等价 SQL `UPDATE … value = value + 1`）。禁止依赖请求早期已跟踪的 Meta 实体做内存 `++` 再写回（防并发丢更新）。禁止靠 `ChangeTracker.HasChanges()` 猜测；禁止业务路径手写 bump。
- dirty bump 成功后：捕获 bump **前**的 version，**异步**调用 `ITagQueryCache.InvalidatePrefix`（按旧 version；Null 为 no-op）。清理失败只记警告日志，**不**使写请求失败。不在写请求上同步清空全库缓存。
- 幂等无变更、同值 PATCH、已对齐的 reorder → dirty=false → HTTP 200，**不** bump；响应仍带**当前** `dataVersion`。
- `POST /v1/relations/batch` 整批成功只 bump **一次**；成功 `data` = `{ "items": [...], "dataVersion": n }`。
- 实体行更新时 `Revision++`；**新建** Tag / Resource / TagRelation 的 `Revision = 0`。

### 4.2 Tag / Alias

- 别名字符集与保留词按[数据模型](./2026-09-23-data-model.md)；违规 → `422 validation_failed`。
- 创建无显式 aliases → 默认 `Alias = Name`（Name 本身非法则创建失败）。
- 创建/改 `Name` → `INameInitialsGenerator` 重算 `NameInitials`（及 `NameNormalized` 按数据模型）；客户端不可写简拼。
- 别名 / 主名冲突 → `409`。
- 禁止删到零个别名 → `422 validation_failed`（`details.reason=last_alias` 建议带上）。

### 4.3 Resource / 打标

- URI 规范化与 Kind（`file` / `folder` / `url`）对齐数据模型；实现位于 Service，不预拆共享包。
- 规范化后若 scheme 与 `kind` 不兼容 → `422 validation_failed`（`details.reason=kind_uri_mismatch`）。`http(s)` 只能 `url`；`file://` 默认 `file`，目录必须显式 `folder`。PATCH 只改 `kind` 时同样校验。
- 打标：未知 uri 隐式建资源；`createMissing: true` 时未知别名自动建标；`add` / `remove` 幂等。
- **仅**插删 `ResourceTag` 时 **不**更新 Resource 的 `UpdatedAt` / `Revision`；有绑定变更则 dirty → bump `data_version`。

### 4.4 Relation

- `child`：`From`=父、`To`=子；同一 `To` 在 `Kind=child` 下至多一父；成环拒绝——写前检测：若沿 `child` 边 `To` 已是 `From` 的祖先则成环。
- 写关系（含 batch）：事务内 **写前再读** 校验单父/成环/唯一；冲突 → `409`（`relation_conflict` / `relation_cycle` 按契约）。
- `related`：禁止自环；允许双向各一条；不强制无环。
- `batch`：在「旧图 ∪ 本批新边」上集合预校验，任一条失败整批失败；`details.index` 报首个违规项。
- `reorder`：必须恰好覆盖该 `(fromTagId, kind)` 下全部 `toTagId`；按数组下标写 `Seq`。
- 删除 Tag：FK Cascade；仍经 `CommitAsync`（有变更则 bump）。

## 5. 查询读路径

### 5.1 `query` / `preview` 流水线

1. 校验 `q`、`expand`、`maxDepth`、`expandedTagLimit` → 形状非法 `400`；超服务端上限 `422`。
2. `NormalizeQuery(q)` → 词面规范化串（段内保序；不写入 expand/分页）。
3. `QueryParser` → clauses；结构非法 → `400 bad_request`。
4. 别名 → `TagId`（只用 `AliasNormalized`）。任一正词未知 → `ServiceResult` Ok + 空列表 + **英文** `Message`（如 `Unknown positive term '{token}'; returning empty result.`）→ HTTP `200`；**不**展开、**不**查集合、**不**走缓存。未知负词忽略并打日志。
5. `expand=child` 时 `QueryExpander`：含自身；深度截断仅日志；去重后触顶 → `422 expansion_limit_exceeded`。
6. 读取当前 `data_version` 组键 → `ITagQueryCache.TryGet`（Null：永未命中）→ miss 则 `QueryExecutor`（EF LINQ 优先）。
7. **`Set` 前复核**：再读一次 `Meta.data_version`；若与组键时的 version **不同** → **不 Set**，仍把本次查库结果返回调用方（不强制按新 version 重查）。
8. **仅成功结果可 Set**（含合法空 `items`）；软失败 / 校验与展开失败等不 Set。若条目超过可配 `MaxEntrySize` → 跳过 Set，仍返回结果。
9. 缓存值为业务 **`data` DTO**（非信封）；`query` 含 `total`；`preview` **省略** `total`。Memory 实现须防御拷贝/不可变后再存（#10）。

**`query` / `preview` 缓存键**：

```text
v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
```

未展开：`expand=none`，`maxDepth=0`，`expandedTagLimit=0`。

**`suggest` 缓存键**（专用，不含 expand 三维）：

```text
v{data_version}:suggest:{normalizedPrefix}:{limit}
```

`normalizedPrefix` = trim 后与别名规范化一致的小写前缀。

键为**不透明相等串**：实现禁止按 `:` split 解析字段（别名可含 `:`，如 `proj:foo`）。格式仅供人读/日志。

### 5.2 `suggest`

- **不**使用查询语法；`AliasNormalized` 前缀 **OR** `NameInitials` 前缀（禁止 `%x%` substring）。
- 经 `ITagQueryCache`；与 query **共用** `QueryCachePipeline`（组键 / TryGet / Set 前 version 复核）；简拚命中时 `alias` 字段规则见 HTTP API。
- v1 **只第一页**：省略 `offset` 或 `offset=0`；`offset>0` → `400 bad_request`。响应仍为列表信封（含 `total`，`offset` 恒 0）。缓存键 **不含** offset。

### 5.3 其它列表读

- `GET /v1/tags`、`GET /v1/resources` 等走对应 Service + 契约排序；**不**强制过查询缓存。
- `GET /v1/relations` **必须** `fromTagId` 或 `toTagId`，否则 `400`。
- 分页形状非法（非整数、负数、`limit=0`、`limit>1024`）→ `400 bad_request`，**不** clamp。`query`/`preview` 省略 `limit` → **50**（不读 `config.previewLimit`）。
- 列表/详情分 DTO：资源列表省略 `tags` 属性（不要靠 `"tags": null`）；preview 省略 `total` 属性。不改信封全局 `Never`。
- 写成功 `data`：实体字段扁平多 `dataVersion`；删除至少 `{ "dataVersion": n }`。

## 6. HTTP 与错误映射

- Endpoint：参数绑定 → 调 Service → 按中央表将 `ServiceResult` 的机器码映射为 HTTP 状态 → `ApiEnvelope`。
- `ServiceResult<T>`：成功可带 `Data` + 可选 `Message`；失败带 code / message / details；**不含** HTTP 状态码整型。
- 预期业务失败**不用**异常表达；未预期异常仍由管道收成 5xx 信封。
- 禁止 ProblemDetails 形状分叉；未知 `/v1` 保持 #8 信封化 404。

## 7. 测试与验收

### 7.1 测试分层

| 层 | 覆盖 |
|---|---|
| 单元 | `QueryParser` / `NormalizeQuery` / 展开闸门；别名字符集；`child` 单父与成环；`NameInitials` 黄金用例；**缓存键工厂**（query/preview/suggest + 未展开占位）；Null 缓存行为 |
| Service / SQLite | 临时文件或可控连接串（**不用** EF InMemory）；打标幂等与 `createMissing`；`relations/batch`；dirty/bump（含并发下 version 单调不丢）；最后别名拒绝；读路径 Set 前 version 变化则不写入缓存 |
| HTTP 集成（#8 fixture） | 鉴权；标签 CRUD；打标；`query`/`preview`；未知正词英文 message；一例 `expand=child`；抽样性能边界（如超限 batch、无过滤 relations） |

### 7.2 完成定义

1. HTTP API 业务路由均已注册且信封一致（含 batch `data` 形状、preview 无 `total`）。
2. 查询行为与查询语言细则一致（含闸门、软失败英文 notice、日志 reason）。
3. 写路径约束（别名 / 最后别名 / 单父 / 成环 / dirty bump / 原子 `data_version+1` / Revision）有自动化测试。
4. [HTTP API §9](./2026-09-26-http-api.md) 性能边界有对应拒绝行为与抽样测试。
5. `ITagQueryCache` 已接线且为 Null；键工厂单测通过；读路径含 Set 前 version 复核钩子（Null 下可测「不调用 Set」分支）；#10 可只换实现。
6. 上述测试本地 / CI 绿灯；实现说明 → `docs/crud-and-query.md`。

## 8. 实现顺序建议

1. 横切：`ServiceResult`、穷尽 `HttpErrorMap`、`IUnitOfWork`（dirty + **原子 bump**）、`INameInitialsGenerator`、别名校验、`UriNormalizer`（含 kind 校验）、键工厂 + Null 缓存 + `QueryCachePipeline`
2. Tags + Aliases + Suggest（无关系）
3. Resources + 打标
4. Relations（约束 / 再读校验 / neighbors / batch / reorder）
5. Query：Parser → Normalize → Expander → Executor + 缓存接线
6. 补齐测试、性能边界抽样、`docs/crud-and-query.md`

## 9. 决策记录（主线）

候选全文见[审核记录 §A](./confirmed/2026-09-29-crud-and-query-review.md)。

| # | 议题 | 候选 | 确认 |
|---|---|---|---|
| Q1 | #9 范围与 #10 | A 全量+Null 缓存 / B 无接口 / C 查询延后 / D 合并 #10 | **A** |
| Q2 | 内部分层 | A Services/ / B 胖 endpoint / C 拆 Core | **A** |
| Q3 | 查询冷路径 | A EF LINQ / B 手写 SQL / C 内存集合 | **A** |
| Q4 | `data_version` | A 集中 Commit / B 各自 bump / C 拦截器；子项 i 有变更才 bump / ii 凡 200 都 bump | **A+i** |
| Q5 | `NameInitials` | A 库+接口 / B 自研表 / C 占位 | **A** |
| Q6 | 测试 | A 分层 / B 全集成 / C 少集成 | **A** |
| Q7 | 查询模块 | A 三分+Normalize / B 单引擎 / C Parser+合并执行 | **A** |

## 10. 决策记录（Grill Round 1–8）

候选全文见[审核记录 §C–§J](./confirmed/2026-09-29-crud-and-query-review.md)。

| Round | # | 议题 | 候选摘要 | 确认 |
|---|---|---|---|---|
| 1 | Q1 | `Revision` | A 行更新++ / B #9 不碰 / C 仅 Tag | **A** |
| 1 | Q2 | 并发单父 | A 事务+再读 / B 接受竞态 / C 全局串行 | **A** |
| 1 | Q3 | dirty 判定 | A 显式 dirty / B ChangeTracker / C 凡 200 bump | **A** |
| 1 | Q4 | 软失败与缓存 | A 不走缓存 / B 缓存空结果 / C TryGet 提前 | **A** |
| 1 | Q5 | suggest 键 | A 专用键 / B 套 query 模板 / C 留给 #10 | **A** |
| 1 | Q6 | 错误形态 | A ServiceResult / B 异常 / C 混用 | **A** |
| 1 | Q7 | batch 成功体 | A items+dataVersion / B 纯数组 | **A** |
| 2 | Q8 | Revision 初值 | A 0 / B 1 | **A** |
| 2 | Q9 | 无变更 dataVersion | A 带回当前 / B 省略 / C 强制 bump | **A** |
| 2 | Q10 | 软成功 Result | A Ok+Message / B SoftOk / C 错误特判 200 | **A** |
| 2 | Q11 | 缓存值 | A data DTO / B 信封 / C Id 列表 | **A** |
| 2 | Q12 | UoW 生命周期 | A Scoped / B 短上下文 / C Singleton | **A** |
| 2 | Q13 | §9 性能边界 | A 进 DoD / B 不进 / C 另开项 | **A** |
| 2 | Q14 | 键工厂测试 | A #9 单测 / B 留给 #10 / C 只测 Null | **A** |
| 3 | Q15 | 键中的 `:` | A 不透明相等 / B 编码可 split / C 改分隔符 | **A** |
| 3 | Q16 | 谁 Commit | A 写用例 / B Endpoint / C 中间件 | **A** |
| 3 | Q17 | Result 含 HTTP？ | A 中央映射表 / B Result 带 status / C endpoint switch | **A** |
| 3 | Q18 | 异步 | A 全 Task+CT / B 缓存 sync / C 能 sync 则 sync | **A** |
| 3 | Q19 | preview total | A 省略 / B 也返回 / C 固定 0 | **A** |
| 3 | Q20 | 实现文档 | A docs/crud-and-query.md / B 改 skeleton 文 / C 不写 | **A** |
| 3 | Q21 | 拼音包钉死？ | A 接口+黄金用例 / B 钉包名 / C 自研 | **A** |
| 4 | Q22 | notice 语言 | A 英文 / B 中文 / C Accept-Language | **A** |
| 4 | Q23 | Service 测库 | A SQLite / B EF InMemory / C 仅 HTTP | **A** |
| 4 | Q24 | 打标碰 Resource？ | A 不碰 / B UpdatedAt+Revision / C 仅 UpdatedAt | **A** |
| 4 | Q25 | 最后别名 | A 422 validation_failed / B 409 / C 400 | **A** |
| 4 | Q26 | 回写范围 | A 规格+review+看板 / B 仅 review / C 新 plan | **A** |
| 5 | Q27 | bump 防丢更新 | A 原子 +1 / B Reload+令牌 / C 接受极少丢失 | **A** |
| 5 | Q28 | Set 前复核 version | A 变则不 Set / B 不复核 / C 整段只读事务 | **A**（不强制重查） |
| 6 | Q29 | Memory 容量 | A SizeLimit+逐出 / B 仅 TTL / C 无硬顶只清前缀 | **A**（**可配**，禁写死） |
| 6 | Q30 | 旧前缀清理 | A 写后异步 InvalidatePrefix / B 只靠逐出 / C 同步 InvalidateAll | **A** |
| 6 | Q31 | TTL 分层 | A suggest/preview 短、query 靠 version / B 同一 TTL / C 全不过期 | **A** |
| 7 | Q32 | 合法空结果 | A 缓存 / B 不缓存空 / C 空更短 TTL | **A** |
| 7 | Q33 | singleflight | A #10 按键合并 / B 不做 / C 全局信号量 | **A** |
| 7 | Q34 | Memory 前缀删 | A 旁路键索引 / B Memory 改 InvalidateAll / C 不真删 | **A** |
| 7 | Q35 | MaxEntrySize | A 超限跳过 Set（**可配**） / B 无单条上限 / C 压缩再存 | **A** |
| 7 | Q36 | 存值形态 | A Memory 拷贝+Redis 字节 / B 统一字节 / C 可变引用 | **A** |
| 7 | Q37 | 错误入缓存 | A 仅成功可 Set / B 可缓存 4xx / C 422 也缓存 | **A** |
| 7 | Q38 | TTL 绝对/滑动 | A 绝对 / B 滑动 / C 混用 | **A** |
| 7 | Q39 | 清理接线 | A #9 Commit 已调 InvalidatePrefix / B #10 再挂 / C 仅 Redis | **A** |
| 8 | Q40 | 配置面 | A Cache:* 可配+推荐默认 / B 仅容量可配 TTL 写死 / C 仅环境变量 | **A** |
| 8 | Q41 | 索引与逐出 | A PostEvictionCallback / B 定期重建 / C 改 InvalidateAll | **A** |
| 8 | Q42 | 防御拷贝手法 | A 不可变/record DTO / B JSON 往返 / C 深拷贝库 | **A** |
| 8 | Q43 | 可观测性 | A 日志/计数 / B 对外 stats API / C 不做 | **A** |
| 8 | Q44 | 清理失败 | A 警告+不失败写 / B 回退 InvalidateAll / C 写 5xx | **A** |

## 11. 实现计划 grill（2026-10-03）

候选与问答全文见[审核记录 §K](./confirmed/2026-09-29-crud-and-query-review.md)。实现计划：[2026-10-03-crud-and-query-impl.md](./2026-10-03-crud-and-query-impl.md)。

| # | 议题 | 确认 |
|---|---|---|
| Q1 | preview 默认 limit | 省略 → **50**；不读 `config.previewLimit` |
| Q2 | 写成功 dataVersion | 实体字段扁平多 `dataVersion`；删除至少 `{ dataVersion }` |
| Q3 | 分页越界 | 形状非法含 `limit=0` / `>1024` → **400**，不 clamp |
| Q4 | 列表省略字段 | 分 DTO；不改全局 `Never` |
| Q5 | commit 前缀 | 递增 `#序号`，不用 backlog `#9` |
| Q6 | Cache:* 键名 | #9 钉死；#10 只绑定 |
| Q7 | kind 与 scheme | 不兼容 → `422`（`kind_uri_mismatch`） |
| Q8 | cache-aside | 共用 `QueryCachePipeline` |
| Q9 | suggest 路由 | `TagEndpoints` |
| Q10 | ErrorCodes 映射 | 穷尽 switch |
| Q11 | suggest offset | 只第一页；`offset>0` → 400；键不含 offset |

