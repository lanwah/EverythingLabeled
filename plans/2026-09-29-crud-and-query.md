# EverythingLabeled CRUD + 查询解析（#9）

日期：2026-09-29  
状态：已确认（v1 实现基线；主线澄清 + 设计 §1–§5 + grill Round 1–4）  
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
- **与 #10**：读路径经 `ITagQueryCache`；#9 提供 **Null 实现**（直通 DB）。Memory / Redis、TTL、旧键清理 → #10。
- **工程**：保持单项目 `EverythingLabeled.Service`；新增 `Services/` 用例层；`Http/*Endpoints` 保持薄；**不**预建 `EverythingLabeled.Core`。
- **数据访问**：写用例集中 `IUnitOfWork.CommitAsync()`（显式 dirty 才 bump）；查询冷路径以 **EF Core LINQ** 为主，难点允许局部 SQL / `FromSql`。
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

- Memory / Redis 缓存实装与失效清理策略细节 → #10
- CLI / `EverythingLabeled.Client` / MCP / 浏览器扩展 / Explorer → #11+
- 多机同步、文件跟标、全盘索引、HTTP MCP（已延后）
- 拆出独立 Core 程序集
- OpenAPI 产物（可选，不堵 #9）
- 按 `Accept-Language` 切换查询 notice 文案

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
│   └── Caching/              # NullTagQueryCache（#9）
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
- `CommitAsync`：同事务 `SaveChanges` +（**显式 dirty** 时）`Meta.data_version++`。禁止靠 `ChangeTracker.HasChanges()` 猜测；禁止业务路径手写 bump。
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
6. `ITagQueryCache.TryGet`（Null：永未命中）→ `QueryExecutor`（EF LINQ 优先）→ `Set`（Null：no-op）。
7. 缓存值为业务 **`data` DTO**（非信封）；`query` 含 `total`；`preview` **省略** `total`。

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
- 经 `ITagQueryCache`；简拚命中时 `alias` 字段规则见 HTTP API。

### 5.3 其它列表读

- `GET /v1/tags`、`GET /v1/resources` 等走对应 Service + 契约排序；**不**强制过查询缓存。
- `GET /v1/relations` **必须** `fromTagId` 或 `toTagId`，否则 `400`。

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
| Service / SQLite | 临时文件或可控连接串（**不用** EF InMemory）；打标幂等与 `createMissing`；`relations/batch`；dirty/bump；最后别名拒绝 |
| HTTP 集成（#8 fixture） | 鉴权；标签 CRUD；打标；`query`/`preview`；未知正词英文 message；一例 `expand=child`；抽样性能边界（如超限 batch、无过滤 relations） |

### 7.2 完成定义

1. HTTP API 业务路由均已注册且信封一致（含 batch `data` 形状、preview 无 `total`）。
2. 查询行为与查询语言细则一致（含闸门、软失败英文 notice、日志 reason）。
3. 写路径约束（别名 / 最后别名 / 单父 / 成环 / dirty bump / Revision）有自动化测试。
4. [HTTP API §9](./2026-09-26-http-api.md) 性能边界有对应拒绝行为与抽样测试。
5. `ITagQueryCache` 已接线且为 Null；键工厂单测通过；#10 可只换实现。
6. 上述测试本地 / CI 绿灯；实现说明 → `docs/crud-and-query.md`。

## 8. 实现顺序建议

1. 横切：`ServiceResult`、code→HTTP 映射、`IUnitOfWork`（dirty）、`INameInitialsGenerator`、别名校验、`UriNormalizer`、缓存键工厂 + Null 缓存
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

## 10. 决策记录（Grill Round 1–4）

候选全文见[审核记录 §C–§F](./confirmed/2026-09-29-crud-and-query-review.md)。

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
