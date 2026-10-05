# #9 CRUD + 查询解析 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已落地的 #8 Service 骨架上实装 HTTP API 全部业务路由、查询语言 `query`/`preview`、`suggest`，以及可被 #10 替换的 Null `ITagQueryCache`。

**Architecture:** 保持单项目 `EverythingLabeled.Service`。`Http/*Endpoints` 只做绑定与信封；用例在 `Services/`；写路径经 Scoped `IUnitOfWork.CommitAsync`（显式 dirty 才原子 bump `data_version`，再异步 `InvalidatePrefix`）。查询为 Parser → Expander → Executor，读路径组键 / TryGet / 查库 / Set 前复核 version。

**Tech Stack:** .NET 8、EF Core 8 + SQLite、Minimal API、xunit、Serilog。拼音库实现时选定（黄金用例锁定行为）。测试：单元不碰库；Service 层用临时 SQLite 文件（禁止 EF InMemory）；HTTP 用现有 `ServiceProcessFixture`。

**Spec:** [plans/2026-09-29-crud-and-query.md](./2026-09-29-crud-and-query.md)（实现边界）。契约全文：[HTTP API](./2026-09-26-http-api.md)、[查询语言](./2026-09-26-query-language.md)、[数据模型](./2026-09-23-data-model.md)。审核：[confirmed/2026-09-29-crud-and-query-review.md](./confirmed/2026-09-29-crud-and-query-review.md)（含实现计划 grill Round 1–3，2026-10-03）。

## Global Constraints

- TFM `net8.0`；`<Nullable>enable</Nullable>`；`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。
- **不**拆 `EverythingLabeled.Core`；依赖方向 `Http` → `Services` → `Data`。
- 公开用例与缓存 `TryGet`/`Set`/`CommitAsync` 一律 `Task` + `CancellationToken`。
- `ServiceResult<T>` **不含** HTTP 状态整型；code → HTTP 中央映射。预期业务失败不用异常。
- 公开写用例结尾 `CommitAsync` **恰好一次**；helper 不 Commit；禁止业务路径手写 bump；禁止 `ChangeTracker.HasChanges()` 猜 dirty。
- bump：同事务 `SaveChanges` + 显式 dirty 时对 `Meta.data_version` **原子 +1**（`ExecuteUpdate` / `UPDATE value = value + 1`）；禁止对早期跟踪的 Meta 做内存 `++`。
- 幂等无变更 / 同值 PATCH / 已对齐 reorder → `dirty=false` → HTTP 200，不 bump，响应仍带**当前** `dataVersion`。
- 新建 Tag / Resource / TagRelation 的 `Revision = 0`；更新该行 `Revision++`。仅插删 `ResourceTag` **不**碰 Resource 的 `UpdatedAt`/`Revision`。
- 查询未知正词：`Ok` + 空列表 + 英文 `Message`：`Unknown positive term '{token}'; returning empty result.`；不展开、不查集合、不走缓存。
- 缓存键为不透明相等串；**禁止**按 `:` split 解析字段。
- #9 只接线 `NullTagQueryCache`；Memory/Redis、singleflight、SizeLimit 实装 → #10。`Cache:*` 键名已钉死（#9 写入 appsettings，#10 只绑定不改名）：`Provider`、`SizeLimit`、`MaxEntrySize`、`SuggestTtl`、`PreviewTtl`。数值：SizeLimit/MaxEntrySize 为**字节整数**；TTL 为 `"00:01:00"`。**禁止把 SizeLimit/MaxEntrySize 写死在业务代码里**。
- Service 测试 **不用** EF InMemory。commit subject 用仓库递增 `#序号`（当前之后为 `#27` 起），**不要**写成 backlog `#9`；conventional commits 不用。
- 路由：字面量段先于 `{id}`。`GET /v1/tags/suggest` 与 `by-alias` 注册在 **`TagEndpoints`**（先于 `{id}`）。`QueryEndpoints` **只**映射 `/v1/query` 与 `/v1/query/preview`。
- 分页：`offset` 默认 0、`limit` 默认 50、上限 1024。非整数、负数、`limit=0`、`limit>1024` → `400 bad_request`（`param_invalid`），**不** clamp。`query` 与 `preview` 省略 `limit` 均为 **50**（**不**读 `AppConfig.PreviewLimit`）。
- `GET /v1/tags/suggest`：只第一页；省略 `offset` 或 `offset=0`；`offset>0` → `400`。响应含 `total`，`offset` 恒为 0。缓存键不含 offset。
- 写成功 `data`：实体字段与读模型相同，**扁平多** `dataVersion`（`TagWriteDto` / `ResourceWriteDto` 等）。删除成功 `data` 至少 `{ "dataVersion": n }`。列表/详情分 DTO（资源列表无 `tags` 属性）；**不**改 `ApiEnvelope` 的 `DefaultIgnoreCondition.Never`。
- URI：规范化后校验 scheme 与 `kind`；不兼容 → `422 validation_failed`，`details.reason=kind_uri_mismatch`。`http(s)` 只能 `url`；`file://` 默认 `file`，目录必须显式 `folder`。
- Suggest 与 query/preview 共用 `QueryCachePipeline`（TryGet → load → 复核 version → 条件 Set）。`HttpErrorMap` 穷尽 `switch`；漏码编译不过；运行期默认分支记 Error 再 500。
- 契约细节以 HTTP API / 查询语言 / 数据模型为准；本计划不覆盖 OpenAPI、CLI、MCP、拆 Core。

---

## File structure

创建（除非注明 Modify）：

```text
src/EverythingLabeled.Service/
├── Hosting/ServiceRegistration.cs          # DI
├── Services/Abstractions/
│   ├── ServiceResult.cs
│   ├── ErrorCodes.cs
│   ├── IUnitOfWork.cs
│   ├── ITagQueryCache.cs
│   ├── INameInitialsGenerator.cs
│   └── Paging.cs                           # offset/limit 校验；Paged<T>
├── Services/UnitOfWork.cs
├── Services/Caching/
│   ├── NullTagQueryCache.cs
│   ├── QueryCacheKeyFactory.cs
│   └── QueryCachePipeline.cs           # TryGet / load / 复核 version / Set
├── Services/Tags/
│   ├── AliasRules.cs
│   ├── NameInitialsGenerator.cs
│   ├── TagDtos.cs
│   └── TagService.cs
├── Services/Resources/
│   ├── UriNormalizer.cs
│   ├── ResourceDtos.cs
│   ├── ResourceService.cs
│   └── ResourceTagService.cs
├── Services/Relations/
│   ├── RelationDtos.cs
│   └── RelationService.cs
├── Services/Query/
│   ├── QueryExpansionLimits.cs
│   ├── QueryModels.cs                      # Clause, QueryParseResult
│   ├── QueryNormalizer.cs                  # NormalizeQuery 纯函数
│   ├── QueryParser.cs
│   ├── QueryExpander.cs
│   ├── QueryExecutor.cs
│   └── QueryService.cs
├── Services/Suggest/SuggestService.cs
├── Http/
│   ├── HttpErrorMap.cs                     # code → status
│   ├── EndpointExtensions.cs               # ServiceResult → ApiEnvelope
│   ├── MetaEndpoints.cs
│   ├── TagEndpoints.cs
│   ├── ResourceEndpoints.cs
│   ├── RelationEndpoints.cs
│   └── QueryEndpoints.cs                   # 仅 /v1/query 与 /preview
tests/EverythingLabeled.Service.Tests/
├── SqliteServiceFixture.cs                 # 临时 el.db + Migrate + 可选 RecordingCache
├── AliasRulesTests.cs
├── NameInitialsGeneratorTests.cs
├── QueryCacheKeyFactoryTests.cs
├── NullTagQueryCacheTests.cs
├── UnitOfWorkTests.cs
├── UriNormalizerTests.cs
├── QueryParserTests.cs
├── QueryNormalizerTests.cs
├── QueryExpanderTests.cs
├── TagServiceTests.cs
├── ResourceTagServiceTests.cs
├── RelationServiceTests.cs
├── QueryServiceCacheHookTests.cs
└── HttpCrudIntegrationTests.cs
docs/crud-and-query.md                      # Task 6 才写
```

Modify:

- `src/EverythingLabeled.Service/Program.cs` — `AddEverythingLabeledServices()`；`MapMeta/Tag/Resource/Relation/QueryEndpoints`。
- `src/EverythingLabeled.Service/EverythingLabeled.Service.csproj` — 拼音 NuGet。
- `src/EverythingLabeled.Service/appsettings.json` — 补全 `Cache:*` 推荐默认。
- `src/EverythingLabeled.Service/Http/ApiEnvelope.cs` — 仅当写回执需要；优先复用现有 `WriteOkAsync`/`WriteErrorAsync`。
- `docs/README.md` — 追加 crud-and-query 行。
- `tests/.../ServiceSkeletonTests.cs` — **不要改行为**；新路由使 `/v1/meta` 从 404 变为 200（现有鉴权测试已打 `/v1/meta`，Task 2 后应仍 401 无 token）。

---

### Task 1: 横切：Result、UoW、简拼、别名、URI、Null 缓存、键工厂

**Files:**

- Create: 上表 `Abstractions/*`、`UnitOfWork.cs`、`Caching/*`、`AliasRules.cs`、`NameInitialsGenerator.cs`、`UriNormalizer.cs`、`QueryExpansionLimits.cs`、`Hosting/ServiceRegistration.cs`、对应测试与 `SqliteServiceFixture.cs`
- Modify: `Program.cs`、`EverythingLabeled.Service.csproj`、`appsettings.json`

**Interfaces:**

- Consumes: `ElDbContext`、`DatabaseInitializer.GetDataVersionAsync`、`ElDbContext.DataVersionKey`
- Produces: 下列签名（后续任务必须原样使用）

```csharp
public sealed class ServiceResult<T>
{
    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? Message { get; }      // 成功时可有 notice
    public string? Code { get; }         // 失败机器码；成功为 null
    public object? Details { get; }
    public static ServiceResult<T> Ok(T data, string? message = null);
    public static ServiceResult<T> Fail(string code, string message, object? details = null);
}

public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string RelationsFilterRequired = "relations_filter_required";
    public const string NotFound = "not_found";
    public const string TagNotFound = "tag_not_found";
    public const string ResourceNotFound = "resource_not_found";
    public const string AliasConflict = "alias_conflict";
    public const string NameConflict = "name_conflict";
    public const string UriConflict = "uri_conflict";
    public const string RelationConflict = "relation_conflict";
    public const string RelationCycle = "relation_cycle";
    public const string ResourceIdUriMismatch = "resource_id_uri_mismatch";
    public const string ValidationFailed = "validation_failed";
    public const string BatchLimitExceeded = "batch_limit_exceeded";
    public const string ExpansionLimitExceeded = "expansion_limit_exceeded";
    public const string Unauthorized = "unauthorized";
    public const string InternalError = "internal_error";
}

public readonly record struct Paging(int Offset, int Limit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 1024;
    // 缺省 offset=0、limit=50。非整数 / 负数 / limit=0 / limit>1024 → Fail(bad_request)。不 clamp。
}

public sealed class Paged<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Offset { get; init; }
    public required int Limit { get; init; }
    public required int Total { get; init; }
}

public interface IUnitOfWork
{
    void MarkDirty();
    Task<int> CommitAsync(CancellationToken cancellationToken);
    // dirty=false: SaveChanges（允许 0 行）后返回当前 data_version，不 bump、不 Invalidate。
    // dirty=true: 同一 SQLite 事务内 SaveChanges + UPDATE Meta SET Value = CAST(Value AS INTEGER)+1
    //   WHERE Key='data_version'; 捕获 bump 前 version；Save 成功后 fire-and-forget
    //   ITagQueryCache.InvalidatePrefixAsync($"v{previous}:", …)；失败只 Warning，不抛给调用方。
    // 返回 bump 后（或未 bump 的当前）data_version。
}

public interface ITagQueryCache
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class;
    Task InvalidatePrefixAsync(string prefix, CancellationToken cancellationToken);
}

public static class QueryCacheKeyFactory
{
    public static string ForQuery(int dataVersion, string op, string normalizedQuery,
        string expand, int maxDepth, int expandedTagLimit, int limit, int offset);
    // v{ver}:{op}:{normalizedQuery}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}
    public static string ForSuggest(int dataVersion, string normalizedPrefix, int limit);
    // v{ver}:suggest:{normalizedPrefix}:{limit}
}

public interface INameInitialsGenerator
{
    string FromName(string name);
}

public static class AliasRules
{
    public static string Normalize(string alias); // trim + ToLowerInvariant
    public static bool TryValidate(string alias, out string reason);
    // reason: alias_rejected_empty | alias_rejected_leading_dash | alias_rejected_whitespace
    //         | alias_rejected_control | alias_rejected_reserved
}

public static class QueryCachePipeline
{
    // load 仅在 miss 时调用。versionAfter != versionAtKey → 不 Set，仍返回 load 结果。
    public static Task<T> GetOrLoadAsync<T>(
        ITagQueryCache cache,
        string key,
        int versionAtKey,
        Func<CancellationToken, Task<int>> readDataVersion,
        Func<CancellationToken, Task<T>> load,
        CancellationToken cancellationToken) where T : class;
}

public static class UriNormalizer
{
    // 规范化字符串；再按 kindHint 与 scheme 校验。不兼容 → Fail(validation_failed, details.reason=kind_uri_mismatch)。
    // kindHint 为空：http(s)→url；file→file（folder 不得仅靠推断）。
    public static ServiceResult<(string Uri, string Kind)> Normalize(string uri, string? kindHint);
}

public static class QueryExpansionLimits
{
    // 与查询语言 §3.1 同源：默认 = 服务端上限
    public const int DefaultMaxDepth = 8;
    public const int ServerMaxDepth = 8;
    public const int DefaultExpandedTagLimit = 512;
    public const int ServerMaxExpandedTagLimit = 512;
}
```

`NullTagQueryCache`：`TryGetAsync` 恒 `null`；`SetAsync`/`InvalidatePrefixAsync` 立即 `Task.CompletedTask`。

`appsettings.json`（#9 不读 SizeLimit 做拒绝逻辑，只落盘占位）：

```json
{
  "Cache": {
    "Provider": "Memory",
    "SizeLimit": 134217728,
    "MaxEntrySize": 2097152,
    "SuggestTtl": "00:01:00",
    "PreviewTtl": "00:01:00"
  },
  "Serilog": { "MinimumLevel": "Information" }
}
```

拼音：`dotnet add package TinyPinyin`（或等价）。`FromName`：汉字 → 拼音首字母小写；ASCII 字母数字 → 小写保留；其余跳过。黄金用例不过则换库，不放宽断言。

`SqliteServiceFixture`：临时目录 + `UseSqlite` 文件 + `DatabaseInitializer.InitializeAsync`；提供 `ElDbContext`、可注入的 `ITagQueryCache`（默认 Null）、真实 `UnitOfWork`。Dispose 关连接并删目录。

- [ ] **Step 1: 写失败单测（键工厂 / Null 缓存 / 别名 / 简拼 / URI）**

```csharp
public sealed class QueryCacheKeyFactoryTests
{
    [Fact]
    public void Query_unexpanded_uses_none_and_zero_placeholders()
    {
        var key = QueryCacheKeyFactory.ForQuery(3, "query", "工作 OR 生活 -归档", "none", 0, 0, 50, 0);
        Assert.Equal("v3:query:工作 OR 生活 -归档:none:0:0:50:0", key);
    }

    [Fact]
    public void Query_key_is_opaque_equality_and_may_contain_colon_in_normalized_query()
    {
        var key = QueryCacheKeyFactory.ForQuery(1, "preview", "proj:foo", "child", 8, 512, 16, 0);
        Assert.Equal("v1:preview:proj:foo:child:8:512:16:0", key);
        Assert.DoesNotContain("split", key); // 工厂本身不得提供 Split 解析 API
    }

    [Fact]
    public void Suggest_key_has_no_expand_dimensions()
    {
        var key = QueryCacheKeyFactory.ForSuggest(2, "gz", 20);
        Assert.Equal("v2:suggest:gz:20", key);
    }
}

public sealed class NullTagQueryCacheTests
{
    [Fact]
    public async Task TryGet_is_always_miss_and_Set_does_not_throw()
    {
        var cache = new NullTagQueryCache();
        Assert.Null(await cache.TryGetAsync<string>("v1:query:a:none:0:0:50:0", CancellationToken.None));
        await cache.SetAsync("k", new { items = Array.Empty<int>() }, CancellationToken.None);
        await cache.InvalidatePrefixAsync("v0:", CancellationToken.None);
        Assert.Null(await cache.TryGetAsync<object>("k", CancellationToken.None));
    }
}

public sealed class AliasRulesTests
{
    [Theory]
    [InlineData("工作")]
    [InlineData("proj:foo")]
    [InlineData("Work")]
    public void Accepts_legal_aliases(string alias) =>
        Assert.True(AliasRules.TryValidate(alias, out _));

    [Theory]
    [InlineData("", "alias_rejected_empty")]
    [InlineData("   ", "alias_rejected_empty")]
    [InlineData("-archived", "alias_rejected_leading_dash")]
    [InlineData("a b", "alias_rejected_whitespace")]
    [InlineData("or", "alias_rejected_reserved")]
    [InlineData("AND", "alias_rejected_reserved")]
    [InlineData("Not", "alias_rejected_reserved")]
    public void Rejects_illegal_aliases(string alias, string reason)
    {
        Assert.False(AliasRules.TryValidate(alias, out var actual));
        Assert.Equal(reason, actual);
    }
}

public sealed class NameInitialsGeneratorTests
{
    private readonly INameInitialsGenerator _gen = new NameInitialsGenerator();

    [Theory]
    [InlineData("工作笔记", "gzbj")]
    [InlineData("API设计", "apisj")]
    [InlineData("ABC", "abc")]
    [InlineData("!!!", "")]
    public void Golden_cases(string name, string expected) =>
        Assert.Equal(expected, _gen.FromName(name));
}

public sealed class UriNormalizerTests
{
    [Fact]
    public void File_drive_upper_slash_no_trailing()
    {
        var r = UriNormalizer.Normalize(@"C:\Users\example\doc.txt", "file");
        Assert.True(r.IsSuccess);
        Assert.Equal("file:///C:/Users/example/doc.txt", r.Data.Uri);
        Assert.Equal("file", r.Data.Kind);
    }

    [Fact]
    public void Https_lower_host_strip_default_port_and_fragment()
    {
        var r = UriNormalizer.Normalize("HTTPS://Example.COM:443/a?x=1#frag", null);
        Assert.True(r.IsSuccess);
        Assert.Equal("https://example.com/a?x=1", r.Data.Uri);
        Assert.Equal("url", r.Data.Kind);
    }

    [Fact]
    public void Https_with_file_kind_is_mismatch()
    {
        var r = UriNormalizer.Normalize("https://example.com/a", "file");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
    }
}
```

控制字符用例：构造含 `\u0001` 的字符串，期望 `alias_rejected_control`。

- [ ] **Step 2: 运行上述测试，确认失败（类型未定义）**

Run: `dotnet test tests/EverythingLabeled.Service.Tests/EverythingLabeled.Service.Tests.csproj --filter "FullyQualifiedName~QueryCacheKeyFactoryTests|FullyQualifiedName~AliasRulesTests|FullyQualifiedName~NameInitialsGeneratorTests|FullyQualifiedName~UriNormalizerTests|FullyQualifiedName~NullTagQueryCacheTests"`

Expected: FAIL（找不到类型）。

- [ ] **Step 3: 最小实现横切类型 + UoW 测试**

`UnitOfWorkTests`（SQLite 文件，两个 `ElDbContext` 并行 dirty Commit）：

```csharp
[Fact]
public async Task Concurrent_dirty_commits_monotonically_increment()
{
    await using var fx = await SqliteServiceFixture.CreateAsync();
    fx.Db.Tags.Add(MinimalTag("a"));
    fx.Uow.MarkDirty();
    await fx.Uow.CommitAsync(CancellationToken.None); // version 1

    await Task.WhenAll(BumpAsync(fx.DbPath), BumpAsync(fx.DbPath));
    await using var check = fx.OpenNewContext();
    Assert.Equal(3, await DatabaseInitializer.GetDataVersionAsync(check, CancellationToken.None));
}

[Fact]
public async Task Clean_commit_does_not_bump_or_invalidate()
{
    var cache = new RecordingCache();
    await using var fx = await SqliteServiceFixture.CreateAsync(cache);
    var v = await fx.Uow.CommitAsync(CancellationToken.None);
    Assert.Equal(0, v);
    Assert.Empty(cache.InvalidatedPrefixes);
}

[Fact]
public async Task Dirty_commit_invalidates_previous_prefix()
{
    var cache = new RecordingCache();
    await using var fx = await SqliteServiceFixture.CreateAsync(cache);
    fx.Db.Tags.Add(MinimalTag("a"));
    fx.Uow.MarkDirty();
    await fx.Uow.CommitAsync(CancellationToken.None);
    await cache.WaitForInvalidateAsync();
    Assert.Contains("v0:", cache.InvalidatedPrefixes);
}
```

`RecordingCache`：实现 `ITagQueryCache`，记录 `Set`/`TryGet`/`InvalidatePrefix` 调用。Invalidate 失败实现：抛异常时 UoW 仍返回成功（另测）。

`UnitOfWork` bump SQL 示意：

```csharp
await _db.Database.ExecuteSqlInterpolatedAsync(
    $"UPDATE Meta SET Value = CAST(Value AS INTEGER) + 1 WHERE Key = {ElDbContext.DataVersionKey}",
    cancellationToken);
```

必须与 `SaveChangesAsync` 包在同一事务（`await using var tx = await _db.Database.BeginTransactionAsync`）。先读 previous version（`GetDataVersionAsync` 或事务内 SELECT），再 SaveChanges，再 bump。

`HttpErrorMap.StatusFor(string code)`：

| code | HTTP |
|---|---|
| bad_request, relations_filter_required | 400 |
| unauthorized | 401 |
| not_found, tag_not_found, resource_not_found | 404 |
| alias_conflict, name_conflict, uri_conflict, relation_conflict, relation_cycle, resource_id_uri_mismatch | 409 |
| validation_failed, batch_limit_exceeded, expansion_limit_exceeded | 422 |
| internal_error | 500 |

`StatusFor` 必须穷尽 `ErrorCodes` 全部常量（switch 无 `default` 吞未知码，或 `_ =>` 仅 `internal_error` 且单测锁死码表）。漏映射 = 编译失败或测试失败，禁止静默当 500 当「还没写映射」。

`EndpointExtensions.WriteAsync(HttpContext, ServiceResult<T>)`：成功 → `ApiEnvelope.WriteOkAsync(data, message)`；失败 → `WriteErrorAsync(StatusFor(code), message, code, details)`。

DI（`ServiceRegistration.AddEverythingLabeledServices`）：

- `ElDbContext` 已 Scoped（#8）
- `IUnitOfWork` → `UnitOfWork` Scoped
- `ITagQueryCache` → `NullTagQueryCache` Singleton
- `INameInitialsGenerator` Singleton
- 本 Task 尚不注册 TagService

`Program.cs` 在 `AddDbContext` 之后调用 `AddEverythingLabeledServices()`。本 Task **不** Map 业务路由。

- [ ] **Step 4: 跑 Task 1 测试至绿灯**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter "FullyQualifiedName~QueryCacheKeyFactoryTests|FullyQualifiedName~NullTagQueryCacheTests|FullyQualifiedName~AliasRulesTests|FullyQualifiedName~NameInitialsGeneratorTests|FullyQualifiedName~UriNormalizerTests|FullyQualifiedName~UnitOfWorkTests"`

Expected: PASS。再跑全量确认 #8 骨架测试仍绿：`dotnet test EverythingLabeled.sln`

- [ ] **Step 5: Commit**

```bash
git add src/EverythingLabeled.Service tests/EverythingLabeled.Service.Tests
git commit -m "#27 横切 ServiceResult、原子 bump UoW、Null 缓存与键工厂"
```

序号以提交时 `git log -1` 的 `#N` +1 为准（此处 `#27` 示意）；后续 Task 的 commit 同样递增，禁止写 backlog `#9`。

---

### Task 2: Tags + Aliases + Suggest（无关系）

**Files:**

- Create: `TagDtos.cs`、`TagService.cs`、`SuggestService.cs`、`Http/TagEndpoints.cs`、`Http/MetaEndpoints.cs`、`TagServiceTests.cs`；扩展 `HttpCrudIntegrationTests` 的 tags/meta/suggest 部分
- Modify: `Program.cs`（Map）、`ServiceRegistration.cs`

**Interfaces:**

- Consumes: Task 1 全部公开类型；实体 `Tag`/`TagAlias`
- Produces:

```csharp
public sealed class TagService
{
    public Task<ServiceResult<Paged<TagDto>>> ListAsync(string? ns, string? q, Paging paging, CancellationToken ct);
    public Task<ServiceResult<TagDto>> GetAsync(Guid id, CancellationToken ct);
    public Task<ServiceResult<TagDto>> GetByAliasAsync(string alias, CancellationToken ct);
    public Task<ServiceResult<TagWriteDto>> CreateAsync(CreateTagRequest body, CancellationToken ct);
    public Task<ServiceResult<TagWriteDto>> PatchAsync(Guid id, PatchTagRequest body, CancellationToken ct);
    public Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct);
    public Task<ServiceResult<TagWriteDto>> AddAliasAsync(Guid id, string alias, CancellationToken ct);
    public Task<ServiceResult<TagWriteDto>> DeleteAliasAsync(Guid id, string alias, CancellationToken ct);
}

public sealed class SuggestService
{
    // paging.Offset 必须为 0（省略视为 0）；>0 → Fail(bad_request)。走 QueryCachePipeline。
    public Task<ServiceResult<Paged<SuggestItemDto>>> SuggestAsync(string q, Paging paging, CancellationToken ct);
}

public sealed record TagDto(
    Guid Id, string Name, string? Namespace, int? Seq, string NameInitials,
    IReadOnlyList<string> Aliases, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision);

public sealed record TagWriteDto(
    Guid Id, string Name, string? Namespace, int? Seq, string NameInitials,
    IReadOnlyList<string> Aliases, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision,
    int DataVersion);

public sealed record DataVersionDto(int DataVersion);

public sealed record SuggestItemDto(Guid TagId, string Alias, string Name, string? Namespace, string NameInitials);
```

写方法：改实体 → 需要持久化变更时 `_uow.MarkDirty()` → `var version = await _uow.CommitAsync(ct)`。创建：`Revision=0`；PATCH 改了字段才 `Revision++` 且更新 `UpdatedAt`。默认别名 = `Name`（须 `AliasRules`）。`Name`/`NameNormalized`/`NameInitials` 在创建与改名时重算。冲突：`AliasNormalized` 全局唯一 → `alias_conflict`；`(Namespace, Name)` → `name_conflict`。删到零个别名 → `validation_failed`，`details: { reason = "last_alias" }`。日志 `alias_rejected_*` 与校验 reason 一致。

列表排序：数据模型 §3.1（Tag.Seq nulls last 语义：`(Seq IS NULL) ASC, Seq ASC, CreatedAt ASC, Id ASC`）。`q` 空白且提供了 q → `bad_request`；无 q = 全表分页。`q` 前缀：**仅** `AliasNormalized LIKE prefix%` **OR** `NameInitials LIKE prefix%`。用户输入的 `%` `_` `\` 必须转义后再拼 LIKE，禁止 `%q%`。

`suggest`：**不**走查询语法。空/空白 `q` → `bad_request`。`offset>0` → `bad_request`。`normalizedPrefix = AliasRules.Normalize(q)`（trim+lower）。经 **`QueryCachePipeline`** + `ForSuggest`（键不含 offset）。简拼命中时 `alias` = 与 `name` 相同的别名，否则该 Tag 别名插入序第一条。

`GET /v1/meta`：`{ dataVersion }`。`GET /v1/tags/by-alias/{alias}`：UrlDecode 后按 `AliasNormalized`。

路由：全部挂 **`TagEndpoints`**。注册顺序：`/v1/tags/suggest`、`/v1/tags/by-alias/{alias}` 先于 `/v1/tags/{id}`。

Neighbors **本 Task 不实现**（Task 4）；`GET .../neighbors` 可暂不 Map。

- [ ] **Step 1: 写 TagService 失败测试**

```csharp
[Fact]
public async Task Create_without_aliases_inserts_default_alias_revision_zero()
{
    await using var fx = await SqliteServiceFixture.CreateAsync();
    var tags = fx.GetTagService();
    var created = await tags.CreateAsync(new CreateTagRequest("工作", null, null, null), CancellationToken.None);
    Assert.True(created.IsSuccess);
    Assert.Equal(0, created.Data!.Revision);
    Assert.Equal("gz", created.Data.NameInitials); // 与黄金用例一致；「工作」→ gz
    Assert.Equal(new[] { "工作" }, created.Data.Aliases);
    Assert.Equal(1, created.Data.DataVersion);
}

[Fact]
public async Task Create_illegal_name_as_default_alias_fails_without_bump()
{
    await using var fx = await SqliteServiceFixture.CreateAsync();
    var r = await fx.GetTagService().CreateAsync(new CreateTagRequest("or", null, null, null), CancellationToken.None);
    Assert.False(r.IsSuccess);
    Assert.Equal(ErrorCodes.ValidationFailed, r.Code);
    Assert.Equal(0, await DatabaseInitializer.GetDataVersionAsync(fx.Db, CancellationToken.None));
}

[Fact]
public async Task Delete_last_alias_is_validation_failed()
{
    // create tag, try delete its only alias
}

[Fact]
public async Task Same_value_patch_does_not_bump()
{
    // PATCH name to the same string → 200, dataVersion unchanged
}

[Fact]
public async Task Prefix_search_does_not_use_substring()
{
    // tags named/aliased "abcdef" vs q=cde → empty; q=abc → hit
}

[Fact]
public async Task Suggest_offset_greater_than_zero_is_bad_request()
{
    // SuggestAsync(q, paging with Offset=1) → bad_request
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test --filter FullyQualifiedName~TagServiceTests`

Expected: FAIL。

- [ ] **Step 3: 实现 TagService / SuggestService / TagEndpoints**

写回执用 `TagWriteDto`（扁平 `dataVersion`）。读列表/详情用 `TagDto`（无 `dataVersion`）。删除用 `DataVersionDto`。

Suggest **必须**调用 `QueryCachePipeline.GetOrLoadAsync`，禁止再手写一套 TryGet/Set。

- [ ] **Step 4: 单测 + HTTP 抽样**

HTTP（`ServiceProcessFixture`）：鉴权后 `POST /v1/tags` → `GET /v1/tags/{id}` → `GET /v1/tags/suggest?q=` → `GET /v1/meta` 的 `dataVersion` 与写后一致；无 Bearer 的 `/v1/meta` 仍 401。

Run: `dotnet test tests/EverythingLabeled.Service.Tests`

Expected: PASS（含 #8）。

- [ ] **Step 5: Commit**

```bash
git commit -m "#28 实现标签 CRUD、别名与 suggest"
```

---

### Task 3: Resources + 打标

**Files:**

- Create: `ResourceDtos.cs`、`ResourceService.cs`、`ResourceTagService.cs`、`Http/ResourceEndpoints.cs`、`ResourceTagServiceTests.cs`
- Modify: `Program.cs`、`ServiceRegistration.cs`

**Interfaces:**

- Consumes: `UriNormalizer`、`TagService` 解析别名的内部方法（抽 `ITagResolver.ResolveByAliasOrId` 避免复制；打标与 Tag 共用）
- Produces:

```csharp
public sealed class ResourceService
{
    public Task<ServiceResult<Paged<ResourceListDto>>> ListAsync(string? kind, string? uriExact, Paging paging, CancellationToken ct);
    public Task<ServiceResult<ResourceDetailDto>> GetAsync(Guid id, CancellationToken ct);
    public Task<ServiceResult<ResourceDetailDto>> GetByUriAsync(string uri, CancellationToken ct);
    public Task<ServiceResult<ResourceWriteDto>> CreateAsync(CreateResourceRequest body, CancellationToken ct);
    public Task<ServiceResult<ResourceWriteDto>> PatchAsync(Guid id, PatchResourceRequest body, CancellationToken ct); // 禁止改 uri；kind 须与现 uri 兼容
    public Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class ResourceTagService
{
    public Task<ServiceResult<TagApplyResultDto>> ApplyAsync(TagApplyRequest body, CancellationToken ct);
    public Task<ServiceResult<Paged<TagDto>>> ListTagsAsync(Guid? resourceId, string? uri, Paging paging, CancellationToken ct);
}

public sealed record ResourceListDto(
    Guid Id, string Uri, string Kind, string? DisplayName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision);

public sealed record ResourceDetailDto(
    Guid Id, string Uri, string Kind, string? DisplayName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision,
    IReadOnlyList<TagDto> Tags);

public sealed record ResourceWriteDto(
    Guid Id, string Uri, string Kind, string? DisplayName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision,
    IReadOnlyList<TagDto> Tags,
    int DataVersion);

public sealed record TagApplyResultDto(
    ResourceDetailDto Resource,
    IReadOnlyList<TagDto> CreatedTags,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed,
    int DataVersion);
```

打标规则（HTTP API §3.4）：`resourceId`/`uri` 至少其一；都有则以 id 为准且须与库存 uri 一致否则 `resource_id_uri_mismatch`。未知 uri → 隐式建资源；kind 显式或由 scheme 推断（http/https→url）；无法推断 → `validation_failed`。规范化后 **kind 与 scheme 不兼容** → `validation_failed`（`kind_uri_mismatch`）。`add`+`remove` 元素总数 >100 → `batch_limit_exceeded`。元素：string=alias 或 `{ "id": guid }`。`createMissing` 仅对 **add 的未知 alias 字符串** 建标；未知 Guid → `tag_not_found`。add/remove **幂等**；无绑定变更则不 MarkDirty。有 `ResourceTag` 插删则 MarkDirty，**不**改 Resource.UpdatedAt/Revision。`TagApplyResultDto.Resource` 用详情形状（含 tags）。

列表 `uri` **仅精确**（先 Normalize 再比）。资源序：CreatedAt ASC, Id ASC。`GET /v1/resources/tags` 与 `/v1/resources/{id}` 字面量优先。

- [ ] **Step 1: 失败测试**

```csharp
[Fact]
public async Task Apply_add_remove_is_idempotent_and_does_not_touch_resource_revision()
{
    // create resource revision 0; apply add alias; revision still 0; data_version +1
    // apply same add again: data_version unchanged
}

[Fact]
public async Task CreateMissing_creates_tag_and_bind()
{
}

[Fact]
public async Task Https_uri_with_file_kind_is_validation_failed()
{
}

[Fact]
public async Task Add_plus_remove_over_100_is_batch_limit()
{
}
```

- [ ] **Step 2: 运行确认失败** — `dotnet test --filter FullyQualifiedName~ResourceTagServiceTests`

- [ ] **Step 3: 实现 Resource* + Endpoints**

- [ ] **Step 4: 测试绿灯** — 全量 `dotnet test`

- [ ] **Step 5: Commit** — 递增序号；正文如 `实现资源 CRUD 与打标`

---

### Task 4: Relations（约束、再读、neighbors、batch、reorder）

**Files:**

- Create: `RelationDtos.cs`、`RelationService.cs`、`Http/RelationEndpoints.cs`、`RelationServiceTests.cs`
- Modify: `TagEndpoints.cs`（补 `GET /v1/tags/{id}/neighbors`）、`Program.cs`

**Interfaces:**

```csharp
public sealed class RelationService
{
    public Task<ServiceResult<Paged<RelationDto>>> ListAsync(Guid? from, Guid? to, string? kind, Paging paging, CancellationToken ct);
    public Task<ServiceResult<RelationDto>> GetAsync(Guid id, CancellationToken ct);
    public Task<ServiceResult<RelationWriteDto>> CreateAsync(CreateRelationRequest body, CancellationToken ct);
    public Task<ServiceResult<RelationBatchResultDto>> CreateBatchAsync(IReadOnlyList<CreateRelationRequest> items, CancellationToken ct);
    public Task<ServiceResult<RelationWriteDto>> PatchSeqAsync(Guid id, int? seq, CancellationToken ct);
    public Task<ServiceResult<DataVersionDto>> DeleteAsync(Guid id, CancellationToken ct);
    public Task<ServiceResult<RelationReorderResultDto>> ReorderAsync(Guid fromTagId, string kind, IReadOnlyList<Guid> orderedToTagIds, CancellationToken ct);
    public Task<ServiceResult<Paged<NeighborDto>>> NeighborsAsync(Guid tagId, string direction, string? kindsCsv, Paging paging, CancellationToken ct);
}

public sealed record RelationBatchResultDto(IReadOnlyList<RelationDto> Items, int DataVersion);

public sealed record RelationWriteDto(
    Guid Id, Guid FromTagId, Guid ToTagId, string Kind, int? Seq,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision,
    int DataVersion);
```

约束：

- kind ∈ {child, related} 否则 `validation_failed`。
- 自环 → `validation_failed`。
- UNIQUE (From, To, Kind) 冲突 → `relation_conflict`。
- `child`：同一 To 已有父 → `relation_conflict`。成环：写前若沿 child 边 **To 已是 From 的祖先**（从 From 沿 child 向上/或从 To 向下到 From）则 `relation_cycle`。检测在**事务内写前再读**。
- `related`：允许双向各一条；不成环检测。
- batch：`items` 长度 1..100；在「旧图 ∪ 本批新边」上集合预校验；任一条失败整批失败；`details.index` + 该边三元组报**第一个**违规项；成功只 bump **一次**。
- reorder：`orderedToTagIds` 必须恰好等于该 `(from, kind)` 全部现有 `toTagId`（集合相等，顺序为新序）；按下标写 Seq 0..n-1；已对齐（Seq 已是 0..n-1 且顺序相同）→ 不 dirty。
- `GET /v1/relations` 缺 from 且缺 to → `relations_filter_required`（400）。仅 to → `CreatedAt ASC, Id ASC`。有 from → 边 Seq 规则。
- 删 Tag：FK Cascade，仍走 TagService.Delete → CommitAsync。
- neighbors：tag 不存在 404；direction 默认 both；kind 缺省两种；对端摘要无 aliases；不嵌套邻域。

- [ ] **Step 1: 失败测试（单父、成环、batch 首个 index、reorder 集合、无变更不 bump）**

```csharp
[Fact]
public async Task Child_second_parent_is_conflict() { }

[Fact]
public async Task Child_cycle_is_relation_cycle()
{
    // A→B, B→C, then C→A
}

[Fact]
public async Task Batch_reports_first_failing_index_and_writes_nothing() { }

[Fact]
public async Task Reorder_same_order_does_not_bump() { }
```

- [ ] **Step 2: 运行确认失败**

- [ ] **Step 3: 实现 RelationService + 路由（literal batch/reorder 先于 `{id}`）**

成环：在内存用旧边 + 本批边建邻接表，对每个新 child 边做「从 To 沿 child 能否走到 From」（或从 From 的祖先集是否包含 To）。batch 与单条共用同一纯函数 `RelationGraph.Validate(existing, proposed)`，避免两套规则。

- [ ] **Step 4: `dotnet test` 全绿**

- [ ] **Step 5: Commit** — 递增序号；正文如 `实现标签关系约束、batch 与 reorder`

---

### Task 5: Query 流水线 + 缓存接线

**Files:**

- Create: `QueryModels.cs`、`QueryNormalizer.cs`、`QueryParser.cs`、`QueryExpander.cs`、`QueryExecutor.cs`、`QueryService.cs`、`Http/QueryEndpoints.cs`、对应 Parser/Normalizer/Expander/QueryServiceCacheHook 测试
- Modify: `Program.cs`

**Interfaces:**

```csharp
public static class QueryNormalizer
{
    public static string Normalize(string q); // 解析 TagId 之前；失败抛或返回需由 Parser 统一 400
}

public sealed class QueryParser
{
    public ServiceResult<ParsedQuery> Parse(string q);
}

public sealed class ParsedQuery
{
    public required string Normalized { get; init; }
    public required IReadOnlyList<QueryClause> Clauses { get; init; }
}

public sealed class QueryClause
{
    public required IReadOnlyList<string> PositiveAliasNormalized { get; init; }
    public required IReadOnlyList<string> NegativeAliasNormalized { get; init; }
}

public sealed class QueryExpander
{
    public Task<ServiceResult<ExpandedQuery>> ExpandAsync(ParsedQuery parsed, IReadOnlyList<ResolvedTerm> resolved, int maxDepth, int expandedTagLimit, CancellationToken ct);
}

public sealed class QueryExecutor
{
    public Task<Paged<QueryHitDto>> ExecuteAsync(ExpandedQuery expanded, Paging paging, bool includeTotal, CancellationToken ct);
}

public sealed class QueryService
{
    public Task<ServiceResult<QueryResultDto>> QueryAsync(QueryRequest req, CancellationToken ct);
    public Task<ServiceResult<PreviewResultDto>> PreviewAsync(QueryRequest req, CancellationToken ct);
}

public sealed record QueryHitDto(Guid Id, string Uri, string Kind, string? DisplayName);
public sealed record QueryResultDto(IReadOnlyList<QueryHitDto> Items, int Offset, int Limit, int Total);
public sealed record PreviewResultDto(IReadOnlyList<QueryHitDto> Items, int Offset, int Limit); // 无 Total
```

流水线（规格 §5.1），顺序不可调换：

1. 校验 `q`、`expand`、`maxDepth`、`expandedTagLimit`：缺 q/空白、非整数/负数、`expandedTagLimit=0`、非法 expand（含 `related`）→ `bad_request`（日志 `param_invalid`）。`maxDepth`/`expandedTagLimit` **大于** ServerMax → `expansion_limit_exceeded`（日志 `param_exceeds_server_max`）。未传 expand：不展开。未传闸门：用 `QueryExpansionLimits` 默认。
2. `Normalize`：Unicode 空白分词；`or` 大小写不敏感 → `OR`；正词/负词小写；段内保序；段间 ` OR `。例：`  工作   or  生活 -归档 ` → `工作 OR 生活 -归档`。
3. `Parse`：OR 首尾、连续 OR、段无正词、孤立 `-` → `bad_request`。`--x`：去掉**一个**前导 `-` 后为负词。
4. 别名 → TagId **只**走 `AliasNormalized`。任一正词未知 → `Ok(空, message)` 英文句式；**不** TryGet/Set、不展开、不 Executor；日志 `unknown_positive_term` + token/段下标/`normalized_query`。未知负词忽略；日志 `unknown_negative_term_ignored`；成功 message 保持 null（除非另有正词 notice）。
5. `expand=child`：含自身；BFS/DFS 沿 child From→To；`depth > maxDepth` 截断只日志 `max_depth_truncated`；正负展开合并去重后 count > limit → `expansion_limit_exceeded`（`expansion_tag_limit_exceeded`），不返回部分结果。
6. 读 version 组键 `ForQuery(..., op query|preview, expand none|child, 未展开时 maxDepth=0 expandedTagLimit=0)` → TryGet；miss 则 Executor（EF LINQ：段内正词「资源带有展开集中至少一标」AND，负词「不得带有展开集中任一标」，段间 OR；并集后 CreatedAt ASC, Id ASC 再 skip/take）。
7. Set 前再读 version；不同 → **不 Set**，仍返回本次结果（不强制重查）。
8. 仅成功可 Set（含合法空 items）。preview **省略 total**。未知正词软成功不 Set。

Executor 优先 LINQ；SQLite 翻译不了时允许局部 `FromSql`。

- [ ] **Step 1: Parser / Normalize / 闸门 / 缓存钩子失败测试**

```csharp
public sealed class QueryNormalizerTests
{
    [Fact]
    public void Preserves_in_clause_order_and_canonical_OR()
    {
        Assert.Equal("工作 OR 生活 -归档", QueryNormalizer.Normalize("  工作   or  生活 -归档 "));
        Assert.Equal("待读 工作", QueryNormalizer.Normalize("待读 工作"));
        Assert.NotEqual(QueryNormalizer.Normalize("工作 待读"), QueryNormalizer.Normalize("待读 工作"));
    }
}

public sealed class QueryParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("OR 工作")]
    [InlineData("工作 OR")]
    [InlineData("工作 OR OR 生活")]
    [InlineData("-归档")]
    [InlineData("工作 OR -归档")]
    [InlineData("-")]
    public void Illegal_structure(string q)
    {
        var r = new QueryParser().Parse(q);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorCodes.BadRequest, r.Code);
    }

    [Fact]
    public void And_or_negatives()
    {
        var r = new QueryParser().Parse("工作 待读 OR 生活 -归档");
        Assert.True(r.IsSuccess);
        Assert.Equal(2, r.Data!.Clauses.Count);
    }
}

public sealed class QueryServiceCacheHookTests
{
    [Fact]
    public async Task Unknown_positive_does_not_touch_cache()
    {
        var cache = new RecordingCache();
        await using var fx = await SqliteServiceFixture.CreateAsync(cache);
        var r = await fx.GetQueryService().QueryAsync(QueryRequest.Parse("no-such-tag"), CancellationToken.None);
        Assert.True(r.IsSuccess);
        Assert.Empty(r.Data!.Items);
        Assert.Equal("Unknown positive term 'no-such-tag'; returning empty result.", r.Message);
        Assert.Empty(cache.TryGetKeys);
        Assert.Empty(cache.SetKeys);
    }

    [Fact]
    public async Task Version_change_before_set_skips_cache_write()
    {
        var cache = new RecordingCache { VersionToReturnOnSecondMetaRead = /* 由夹具在 Executor 后 bump */ };
        // 实现：RecordingCache 不负责 Meta；用 callback cache 在 Set 前由测试把 Meta +1
        // 或 QueryService 注入 IDataVersionReader 可 stub。优先：包装 Db 第二次 GetDataVersion 返回 version+1。
        Assert.Empty(cache.SetKeys);
        Assert.NotEmpty(/* 返回的 items 仍是查库结果，可非空 */);
    }

    [Fact]
    public async Task Successful_empty_intersection_may_set()
    {
        // 两个已知正词 AND 无交集 → items 空但仍 Set
    }
}
```

闸门：`expand=foo` 400；`maxDepth=9` 422；`expandedTagLimit=0` 400。

Expander：链深度 3、`maxDepth=1` 不 422 且日志路径可测（可用测试 logger sink，或 Expander 返回 `Truncated` 标志仅供测试——**不要**把截断写入 HTTP message）。触顶：人为 `expandedTagLimit=1` 且 expand 出 2 个 id → 422。

- [ ] **Step 2: 运行确认失败**

- [ ] **Step 3: 实现四分组件 + QueryEndpoints**

`GET /v1/query` 与 `GET /v1/query/preview`。省略 `limit` 均为 **50**（与 `Paging.DefaultLimit` 相同；**不**读 `AppConfig.PreviewLimit`）。QueryService 经 **`QueryCachePipeline`** 做 TryGet/Set。

- [ ] **Step 4: `dotnet test` 全绿**

- [ ] **Step 5: Commit** — 递增序号；正文如 `实现查询解析、展开与冷路径执行`

---

### Task 6: HTTP 集成抽样、性能边界、实现说明

**Files:**

- Create: `docs/crud-and-query.md`；补全 `HttpCrudIntegrationTests.cs`
- Modify: `docs/README.md`

**Interfaces:** 无新业务类型。

HTTP 集成（`ServiceProcessFixture` + Bearer）至少覆盖：

1. 无/错 token → 401 `unauthorized`（已有骨架；确认业务路由同样）。
2. 标签 CRUD 一轮（create / get / patch / alias / delete）。
3. 打标 add/remove 幂等 + `createMissing`。
4. `GET /v1/query` 与 `preview`：合法 AND/OR/`-tag`；未知正词英文 message + 空 items；preview JSON **无** `total` 属性。
5. 一例 `expand=child`（父标查询命中只打了子标的资源）。
6. `POST /v1/relations/batch` 成功体为 `{ items, dataVersion }`。
7. 性能边界抽样：`add+remove` 101 → 422 `batch_limit_exceeded`；relations batch 0 或 101 → 同码；`GET /v1/relations` 无过滤 → 400 `relations_filter_required`；`suggest?q=` 空白 → 400；`GET /v1/tags?q=` 空白 → 400；`suggest?q=工作&offset=1` → 400；`limit=0` 或 `limit=1025` → 400。
8. 写回执含扁平 `dataVersion`；preview JSON **无** `total`；资源列表 JSON **无** `tags` 属性。

`docs/crud-and-query.md` 写实现说明（目录、写路径 bump、查询流水线、Null 缓存、如何跑测试）；**链到** plans，不抄契约全文。`docs/README.md` 表追加一行 #9。

- [ ] **Step 1: 写集成测试（可先编译失败若缺路由）**
- [ ] **Step 2: 跑集成测试，修到绿**
- [ ] **Step 3: 撰写 `docs/crud-and-query.md` 并更新 README**
- [ ] **Step 4: `dotnet test EverythingLabeled.sln` 全绿**
- [ ] **Step 5: Commit** — 递增序号；正文如 `补齐 HTTP 验收与 crud-and-query 说明`

---

## Mapping 备注（执行时用，勿另发明）

**LIKE 前缀：** `EscapeLike(s)` 将 `\`, `%`, `_` 加上转义，使用 `EF.Functions.Like(col, prefix + "%", "\\")`。

**Tag 列表 Seq：** SQLite 上 `(t.Seq == null)` 可翻译；与数据模型 SQL 等价。

**JSON 时间：** 现有 `JsonNamingPolicy.CamelCase`；`DateTimeOffset` 默认 ISO-8601。Guid 标准字符串。

**Minimal API body：** `add` 异构数组用 `JsonElement` 或自定义 `TagRefJsonConverter`（string | `{id}`）。

**异步 Invalidate：** `UnitOfWork` 捕获 prefix 后 `_ = Task.Run(async () => { try { await cache.InvalidatePrefixAsync(prefix, CancellationToken.None); } catch (Exception ex) { Log.Warning(ex, "InvalidatePrefix failed"); } });` 不得引用 Scoped DbContext。缓存为 Singleton，合法。

**#10 钩子已满足：** 换 `ITagQueryCache` 实现即可；读路径已有 TryGet / Set / version 复核 / 合法空可 Set / 软失败不 Set。MaxEntrySize 跳过 Set 在 Null 下无意义；#10 在 Memory 的 `SetAsync` 内实现。

---

## Self-review

**Spec coverage**

| 规格 | Task |
|---|---|
| §2.1 Tags CRUD/aliases | 2 |
| §2.1 Resources + 打标 | 3 |
| §2.1 Relations + neighbors | 4 |
| §2.1 query/preview | 5 |
| §2.1 suggest | 2（读路径）+ 5 闸门无关 |
| §2.1 meta + data_version | 1 UoW + 2 endpoint |
| §2.1 缓存桩 + 键工厂 | 1 |
| §2.1 HTTP §9 边界 | 3/4 单测 + 6 抽样 |
| §2.3 InvalidatePrefix 接线 | 1 |
| §2.3 appsettings Cache:* | 1 |
| §4 原子 bump、dirty、Revision、打标不碰 Resource | 1 + 3 |
| §4 别名/最后别名 | 2 |
| §4 关系再读/成环/batch | 4 |
| §5 流水线与 Set 前复核 | 1 pipeline + 2 suggest + 5 query |
| §6 ServiceResult + 中央映射 | 1 + endpoints |
| 实现计划 grill Q1–Q11 | Global Constraints + 各 Task DTO/路由 |

**Placeholder scan:** 无 TBD。拼音包名允许实现时替换，但黄金用例不可删。

**Type consistency:** `ServiceResult<T>`、`Paging`、`QueryCacheKeyFactory`、`QueryCachePipeline`、`IUnitOfWork.CommitAsync → int`、`TagWriteDto` / `ResourceWriteDto` / `RelationWriteDto`、`DataVersionDto`、`RelationBatchResultDto` 在后续任务中同名。

**非目标确认：** Memory/Redis 实装、CLI、Core 拆分、OpenAPI、Accept-Language notice 均不在任务中。
