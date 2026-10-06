# CRUD 与查询（#9）实现说明

日期：2026-10-05  
状态：已实现  
规格：

- [plans/2026-09-29-crud-and-query.md](../plans/2026-09-29-crud-and-query.md)
- [plans/2026-10-03-crud-and-query-impl.md](../plans/2026-10-03-crud-and-query-impl.md)
- HTTP 契约：[plans/2026-09-26-http-api.md](../plans/2026-09-26-http-api.md)
- 查询语言：[plans/2026-09-26-query-language.md](../plans/2026-09-26-query-language.md)

本文件只描述**已落地的实现布局与运行方式**；请求/响应字段与错误码以 plans 为准，不在此抄契约全文。

## 1. 目录布局（相对 `src/EverythingLabeled.Service/`）

```text
Services/
├── Abstractions/          # ServiceResult、Paging、ErrorCodes、IUnitOfWork、ITagQueryCache …
├── Caching/            # Memory/Redis/Null、Pipeline、Singleflight、Sizer、Metrics
├── Tags/               # TagService、SuggestService、别名/简拼规则
├── Resources/          # ResourceService、ResourceTagService、UriNormalizer
├── Relations/          # RelationService、RelationGraph（成环/单父/batch 上限）
├── Query/              # Parser / Expander / Executor / QueryService
└── UnitOfWork.cs       # dirty → bump data_version → 异步 InvalidatePrefix
Http/
├── TagEndpoints / ResourceEndpoints / RelationEndpoints / QueryEndpoints / MetaEndpoints
└── EndpointExtensions、ApiEnvelope、HttpErrorMap
```

DI 集中在 `ServiceRegistration`；`Program` 挂载上述 Map*Endpoints。骨架启动/鉴权见 [service-skeleton.md](./service-skeleton.md)。

## 2. 写路径与 `dataVersion`

成功写操作经 `IUnitOfWork`：

1. 业务改动后 `MarkDirty()`（无变更则可不 Mark，仍可 `CommitAsync` 读当前版本）。
2. `CommitAsync`：若 dirty，原子递增 Meta `data_version`，再 `SaveChanges`。
3. 捕获当前缓存 key 前缀后，**异步** `InvalidatePrefixAsync`（不引用 Scoped `DbContext`；失败仅打 Warning）。

写回执 DTO（如 `TagWriteDto`、`TagApplyResultDto`、`RelationBatchResultDto`）在 `data` **扁平**携带 `dataVersion`。只读 GET 详情/列表**不含** `dataVersion`；`GET /v1/meta` 提供当前版本。

## 3. 查询流水线

`GET /v1/query` 与 `GET /v1/query/preview` 共用 `QueryService.RunAsync`：

1. 闸门：`expand` / `maxDepth` / `expandedTagLimit`（见 HTTP 规格）。
2. `QueryParser` → 规范化 + AND/OR/`-tag` 子句。
3. 解析别名：未知**正词** → `200 ok` + 英文 `message` + 空 `items`（不写缓存）。
4. 可选 `expand=child`（`QueryExpander`）；`related` 不参与集合展开。
5. `QueryCachePipeline`：TryGet → 冷路径 `QueryExecutor` → 版本复核后 Set。
6. preview 映射为 `PreviewResultDto`：**JSON 无 `total` 属性**；query 含 `total`。

## 4. 缓存

默认 **Memory**（`Cache:Provider=Memory`）。`Provider=Null` 永不命中。接线与 Redis 运维见 [query-cache.md](./query-cache.md)。

读路径（suggest / query / preview）走实例 `QueryCachePipeline` + `QueryCacheKeyFactory`；写后按旧 `v{n}:` 前缀失效。

## 5. 资源列表形状

`GET /v1/resources` 的 item 为 `ResourceListDto`：**无 `tags`**。资源上的标签见详情 / `GET /v1/resources/tags` / 打标回执。

## 6. 本地运行与测试

```powershell
# 仓库根目录（或 build.bat / .\build.ps1，见 [文档首页](./README.md#编译)）
dotnet build EverythingLabeled.sln
dotnet run --project src/EverythingLabeled.Service

# 全量测试（含 ServiceProcessFixture HTTP 集成）
dotnet test EverythingLabeled.sln

# 仅 HTTP 集成抽样
dotnet test EverythingLabeled.sln --filter FullyQualifiedName~HttpCrudIntegrationTests
```

集成测试用 `EL_APPDATA` / `EL_MUTEX_NAME` 隔离临时目录，Bearer 取自 fixture 写入的 `config.json`。

## 7. 相关文档

| 文档 | 用途 |
|---|---|
| [service-skeleton.md](./service-skeleton.md) | #8 启动、鉴权、健康检查 |
| [HTTP API plan](../plans/2026-09-26-http-api.md) | 路由与错误码权威 |
| [实现计划](../plans/2026-10-03-crud-and-query-impl.md) | Task 分解与约束 |
