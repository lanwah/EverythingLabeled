# EverythingLabeled CRUD + 查询解析（#9）

日期：2026-09-29  
状态：已确认（v1 实现基线；主线澄清 + 设计 §1–§5）  
审核记录：[CRUD + 查询解析讨论确认](./confirmed/2026-09-29-crud-and-query-review.md)  
关联：

- [Service 骨架](./2026-09-27-service-skeleton.md)（#8 已实现）
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)（§5 查询缓存）
- [路线图 backlog #9](./2026-09-23-roadmap-backlog.md)

契约细节（路由 JSON、词法、闸门、错误码）以关联已确认文档为准；本文只定 **#9 实现边界、分层与验收**，不重复抄全文。

## 1. 设计摘要

- **范围**：在 #8 骨架上实装 [HTTP API](./2026-09-26-http-api.md) **全部业务路由** + [查询语言](./2026-09-26-query-language.md) 的 `query` / `preview`，以及 `suggest`（别名 + `NameInitials` 前缀）。
- **与 #10**：读路径经 `ITagQueryCache`；#9 提供 **Null 实现**（直通 DB）。Memory / Redis、TTL、旧键清理 → #10。
- **工程**：保持单项目 `EverythingLabeled.Service`；新增 `Services/` 用例层；`Http/*Endpoints` 保持薄；**不**预建 `EverythingLabeled.Core`。
- **数据访问**：写路径集中 `IUnitOfWork.CommitAsync()`；查询冷路径以 **EF Core LINQ** 为主，难点允许局部 SQL / `FromSql`。
- **简拼**：NuGet 拼音库封装为 `INameInitialsGenerator`；用例表锁定行为。

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
| 缓存桩 | `ITagQueryCache` + `NullTagQueryCache` 已接线 |

### 2.2 非目标

- Memory / Redis 缓存实装与失效清理策略细节 → #10
- CLI / `EverythingLabeled.Client` / MCP / 浏览器扩展 / Explorer → #11+
- 多机同步、文件跟标、全盘索引、HTTP MCP（已延后）
- 拆出独立 Core 程序集

## 3. 目录与组件

```text
src/EverythingLabeled.Service/
├── Services/
│   ├── Abstractions/          # IUnitOfWork, ITagQueryCache, INameInitialsGenerator, …
│   ├── Tags/
│   ├── Resources/            # 含 UriNormalizer（#9 仅 Service 内；日后 Client 再对齐/抽取）
│   ├── Relations/
│   ├── Query/                # QueryParser, QueryExpander, QueryExecutor, NormalizeQuery
│   ├── Suggest/
│   └── Caching/              # NullTagQueryCache（#9）
├── Http/                     # *Endpoints：绑定 → Service → ApiEnvelope
├── Data/                     # 已有
├── Auth/ / Hosting/ / Logging/
```

**依赖方向**：`Http` → `Services` → `Data`。查询内部：Parser → Expander → Executor；`NormalizeQuery` 为独立纯函数（解析 TagId **之前**，供缓存键；#10 复用）。

**DI**：上述类型在 `Hosting` 注册；`ITagQueryCache` → Null 实现。

**常量**：`QueryExpansionLimits`（`maxDepth` / `expandedTagLimit` 默认与服务端上限）集中定义，注释对齐[查询语言 §3.1](./2026-09-26-query-language.md)。

## 4. 写路径

### 4.1 `data_version`

- 所有写用例经 `IUnitOfWork.CommitAsync()`：同事务 `SaveChanges` +（**仅当本事务确有持久化业务变更**时）`Meta.data_version++`。
- 幂等无变更（如 `add` 已存在绑定）→ HTTP 200，**不** bump。
- `POST /v1/relations/batch` 整批成功只 bump **一次**。
- 业务路径禁止各自手写 bump。
- 写响应在契约要求处带回 `dataVersion`。

### 4.2 Tag / Alias

- 别名字符集与保留词按[数据模型](./2026-09-23-data-model.md)；违规 → `422 validation_failed`。
- 创建无显式 aliases → 默认 `Alias = Name`（Name 本身非法则创建失败）。
- 创建/改 `Name` → `INameInitialsGenerator` 重算 `NameInitials`；客户端不可写简拼。
- 别名 / 主名冲突 → `409`。

### 4.3 Resource / 打标

- URI 规范化与 Kind（`file` / `folder` / `url`）对齐数据模型；实现位于 Service，不预拆共享包。
- 打标：未知 uri 隐式建资源；`createMissing: true` 时未知别名自动建标；`add` / `remove` 幂等。

### 4.4 Relation

- `child`：`From`=父、`To`=子；同一 `To` 在 `Kind=child` 下至多一父；成环拒绝——写前检测：若沿 `child` 边 `To` 已是 `From` 的祖先则成环。
- `related`：禁止自环；允许双向各一条；不强制无环。
- `batch`：集合预校验，任一条失败整批失败（`409 relation_conflict` 等按契约）。
- `reorder`：重排同一 `(fromTagId, kind)` 下 `Seq`。
- 删除 Tag：依赖现有 FK Cascade；仍经 `CommitAsync`（有变更则 bump）。

## 5. 查询读路径

### 5.1 `query` / `preview` 流水线

1. 校验 `q`、`expand`、`maxDepth`、`expandedTagLimit` → 形状非法 `400`；超服务端上限 `422`。
2. `NormalizeQuery(q)` → 词面规范化串（段内保序；不写入 expand/分页）。
3. `QueryParser` → clauses；结构非法 → `400 bad_request`。
4. 别名 → `TagId`（只用 `AliasNormalized`）。任一正词未知 → HTTP `200`、`status: ok`、空 `items`、非空 `message`；**不**展开、**不**查集合。未知负词忽略并打日志。
5. `expand=child` 时 `QueryExpander`：含自身；`depth > maxDepth` 截断（日志，不进成功 `message`）；整次查询展开 TagId 去重后触顶 → `422 expansion_limit_exceeded`。
6. `ITagQueryCache.TryGet`（Null：永未命中）→ `QueryExecutor`（EF LINQ 优先）：段内正词 AND、负词排除、段间 OR；并集后按 `CreatedAt`、`Id` 升序再 `offset`/`limit`。
7. `ITagQueryCache.Set`（Null：no-op）。

缓存键格式（#9 生成键、#10 填充实现）：

```text
v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
```

未展开占位：`expand=none`，`maxDepth=0`，`expandedTagLimit=0`（见[查询语言 §4](./2026-09-26-query-language.md)）。

### 5.2 `suggest`

- **不**使用查询语法；`AliasNormalized` 前缀 **OR** `NameInitials` 前缀。
- 同样经 `ITagQueryCache`（`op=suggest`）。

### 5.3 其它列表读

- `GET /v1/tags`、`GET /v1/resources` 等走对应 Service + 契约排序；**不**强制过查询缓存。

## 6. HTTP 与错误映射

- Endpoint 只做参数绑定、调用 Service、写 `ApiEnvelope`。
- Service 使用可判别结果（或少量 typed 错误）；映射到契约 `data.code`（如 `tag_not_found`、`validation_failed`、`relation_conflict`、`expansion_limit_exceeded`、`bad_request`、`unauthorized`）。
- 禁止 ASP.NET ProblemDetails 形状分叉；未知 `/v1` 路由保持 #8 信封化 404。

## 7. 测试与验收

### 7.1 测试分层

| 层 | 覆盖 |
|---|---|
| 单元 | `QueryParser` / `NormalizeQuery` / 展开闸门与截断；别名字符集；`child` 单父与成环；`NameInitials` 用例表 |
| Service / 内存 SQLite | 打标幂等与 `createMissing`；`relations/batch` 整批失败；`data_version` 有变更才 +1 |
| HTTP 集成（沿用 #8 fixture） | 鉴权；标签 CRUD；打标；`query`/`preview` 主路径；未知正词信封；至少一例 `expand=child` |

### 7.2 完成定义

1. HTTP API 业务路由均已注册且信封一致。
2. 查询行为与查询语言细则一致（含闸门与软失败）。
3. 写路径约束（别名 / 单父 / 成环 / bump）有自动化测试。
4. `ITagQueryCache` 已接线且为 Null；#10 可只换实现而不改流水线骨架。
5. 上述测试本地 / CI 绿灯。

## 8. 实现顺序建议

1. 横切：`IUnitOfWork`、错误结果类型、`INameInitialsGenerator`、别名校验、`UriNormalizer`
2. Tags + Aliases + Suggest（无关系）
3. Resources + 打标
4. Relations（约束 / neighbors / batch / reorder）
5. Query：Parser → Normalize → Expander → Executor + Null 缓存接线
6. 补齐 HTTP 集成测与验收清单

## 9. 决策记录（主线）

| # | 议题 | 确认 |
|---|---|---|
| Q1 | #9 范围与 #10 边界 | **A** — 全量业务 API + 查询引擎；`ITagQueryCache` 空实现 |
| Q2 | 内部分层 | **A** — 单项目 + `Services/`；endpoint 薄 |
| Q3 | 查询冷路径 | **A** — EF LINQ 优先；难点可局部 SQL |
| Q4 | `data_version` | **A+i** — 集中 `CommitAsync`；仅有真实变更才 bump |
| Q5 | `NameInitials` | **A** — 拼音库 + `INameInitialsGenerator` |
| Q6 | 测试 | **A** — 单元 + Service/SQLite + 关键 HTTP 集成 |
| Q7 | 查询模块拆分 | **A** — Parser / Expander / Executor + `NormalizeQuery` |
