# CLI（#11）实现计划讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 1–3  
已落盘同步：

- [CLI 规格](../2026-09-26-cli.md)（产品规格不改；[规格审核](./2026-09-26-cli-review.md)）
- [CLI 实现计划](../2026-10-06-cli-impl.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#11 已确认实现计划）
- [项目看板](../../project.md)

前提（未作为开放题，与规格/架构一致）：不做标签 CRUD、关系、`health` 子命令、安装器、写 config、`--timeout`；`previewLimit` / `maxConcurrency` 给 Explorer / 扩展，CLI 忽略；`preview`/`suggest` 省略 `--limit` 时显式传 **10**；`untag` 确认只在 CLI；开发期 framework-dependent。

---

## A. Round 1 — 交付与分层

### Q1 — #11 交付边界

**问题**：本轮做到哪一层？是否顺带 MCP / 安装？

| 选项 | 含义 |
|---|---|
| **A** | `EverythingLabeled.Client`（config、`IElApi`/`HttpElApi`、PathUri/`kind`）+ `EverythingLabeled.Cli`（`el`：命令、人类/`--json`、退出码、TTY 确认、`completion powershell`）+ 对应测试。不装到 `%LocalAppData%`，不做 MCP host / Explorer |
| B | 只做 `el`，Client 仅内部类、不对外 `IElApi`（#12 再抽） |
| C | A + 本轮顺带 MCP stdio host（#12） |

**确认**：选推荐 **A**。

### Q2 — `IElApi` 覆盖哪些 HTTP

**问题**：Client 要当通用 SDK 还是只覆盖已确认客户端命令面？

| 选项 | 含义 |
|---|---|
| **A** | 仅已确认客户端命令面：`tag` / `untag` / `tags` / `query` / `preview` / `suggest` + `health`（给 MCP，CLI 不暴露）。不要 tags/resources/relations CRUD |
| B | 全量 `/v1` CRUD |
| C | 仅 CLI 七个动词，不含 `health` |

**确认**：选推荐 **A**。

### Q3 — 路径 → URI 放哪

**问题**：Service 已有 `UriNormalizer`；Explorer 要求与 CLI 同一实现库。

| 选项 | 含义 |
|---|---|
| **A** | 新建小库 `EverythingLabeled.PathUri`：纯 URI 规范化从 Service 迁入；Client 再包一层文件系统/`kind`。Service 改为引用该库，行为不变 |
| B | 规范化只放 Client；Service 保持自己的 `UriNormalizer` |
| C | Service 引用整个 Client（仅为了 URI） |

**确认**：选推荐 **A**。

### Q4 — CLI 解析器

**问题**：用哪个命令行库？

| 选项 | 含义 |
|---|---|
| **A** | `System.CommandLine` **2.0.12** |
| B | Spectre.Console.Cli |
| C | 手写 `args` 解析 |

**确认**：选推荐 **A**。不跟 3.0 RC。

### Q5 — Client 错误模型

**问题**：本地码与 HTTP 错误如何表达？

| 选项 | 含义 |
|---|---|
| **A** | `ElResult<T>` + `ElError(code, message, httpStatus?)`；超时/连不上 → `connection_failed`。CLI 映射 exit 1/2/3/4。不用异常表达预期失败 |
| B | 抛 `ElException`，CLI `catch` 映射 |
| C | HTTP 层抛，本地校验返回 Result |

**确认**：选推荐 **A**。

### Q6 — 测试怎么接 HTTP

**问题**：Client 是否打真 Service？

| 选项 | 含义 |
|---|---|
| **A** | Client：`HttpMessageHandler` 假件。CLI：假 `IElApi`。PathUri：字符串与可测的文件系统夹具。不让 Client.Tests 引用 Service |
| B | `WebApplicationFactory` 打真 Service |
| C | WireMock 独立进程 |

**确认**：选推荐 **A**。

### Q7 — `el completion powershell`

**问题**：补全脚本是否运行时调 suggest？

| 选项 | 含义 |
|---|---|
| **A** | 离线 stdout 打印脚本；注册后对 alias/`q`/`prefix` 调 `el suggest`（失败静默空） |
| B | 只打印 SCL 默认补全，运行时不调 suggest |
| C | 脚本内嵌 HttpClient 直打 API |

**确认**：选推荐 **A**。

---

## B. Round 2 — 输出、签名、工程

### Q8 — `tag` / `untag` 人类 stdout

**问题**：API `added`/`removed` 是 tagId，人类输出要别名。

| 选项 | 含义 |
|---|---|
| **A** | 用本次请求 alias，对照 `resource.tags` + `createdTags` 把 id 映回别名。有变更：`added: a, b` 或 `removed: a, b`（用户参数顺序）。`createdTags` 非空另起一行 `created: name1, name2`（`Tag.Name`）。皆空：一行 `unchanged` |
| B | 只打数量：`added: 2` |
| C | 把用户 alias 原样打出，不管是否真写入 |

**确认**：选推荐 **A**。

### Q9 — `el tags` 打什么

**问题**：`GET /v1/resources/tags` 返回 `TagDto`（含 `aliases[]`）。

| 选项 | 含义 |
|---|---|
| **A** | 按 API 标签序，再按每个 `aliases` 序，每个别名一行。不打 Namespace |
| B | 每行一个 `Tag.Name` |
| C | 每行 `alias\tname` |

**确认**：选推荐 **A**。

### Q10 — `query` / `preview` / `suggest` 行格式

**问题**：人类模式 stdout/stderr 钉死成什么？

| 选项 | 含义 |
|---|---|
| **A** | 命中：每行 `displayName`，缺则 URI。`query` stderr：`offset={n} limit={n} total={n}`。`preview`：`offset={n} limit={n}`。`suggest`：`{alias}\t{name}\t{nameInitials}`。非空 API `message` → stderr 一行原文 |
| B | query 每行 `displayName\turi` |
| C | suggest 只打 alias |

**确认**：选推荐 **A**。

### Q11 — `el tags` 分页

**问题**：CLI 无 `--limit`，API 有分页上限 1024。

| 选项 | 含义 |
|---|---|
| **A** | Client `GetTags` 翻页直到取完（每页 limit=1024） |
| B | 只拉第一页，多的丢掉 |
| C | CLI 增加 `--limit` / `--offset` |

**确认**：选推荐 **A**。

### Q12 — `IElApi` 方法

**问题**：签名是否收未规范化 target、是否共享 Contracts 工程？

推荐签名（Client 自有 DTO）：

```csharp
Task<ElResult<TagApplyData>> TagAsync(string uri, string kind, IReadOnlyList<string> aliases, bool createMissing, CancellationToken ct = default);
Task<ElResult<TagApplyData>> UntagAsync(string uri, IReadOnlyList<string> aliases, CancellationToken ct = default);
Task<ElResult<IReadOnlyList<TagInfo>>> GetTagsAsync(string uri, CancellationToken ct = default);
Task<ElResult<QueryPage>> QueryAsync(string q, string? expand, int? maxDepth, int? expandedTagLimit, int limit, int offset, CancellationToken ct = default);
Task<ElResult<PreviewPage>> PreviewAsync(string q, string? expand, int? maxDepth, int? expandedTagLimit, int limit, CancellationToken ct = default);
Task<ElResult<IReadOnlyList<SuggestItem>>> SuggestAsync(string prefix, int limit, CancellationToken ct = default);
Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default);
```

Path 解析、存在性、`kind`、config、untag 确认不在 `IElApi`。`expand` 仅 `null` 或 `"child"`。闸门校验在 CLI。

| 选项 | 含义 |
|---|---|
| **A** | 按上面签名钉死；DTO 是 Client 自己的 record；JSON camelCase；不建 Contracts 工程 |
| B | `IElApi` 收未规范化 `target`，内部做 PathUri |
| C | 共享 Contracts 工程给 Service + Client |

**确认**：选推荐 **A**。

### Q13 — 工程与测试切分

**问题**：几个 csproj？

| 选项 | 含义 |
|---|---|
| **A** | `src/EverythingLabeled.PathUri`、`Client`、`Cli`（`AssemblyName=el`）。测试：`PathUri.Tests`、`Client.Tests`、`Cli.Tests`。`UriNormalizerTests` 迁到 PathUri.Tests；Service 薄封装继续 `ServiceResult` |
| B | PathUri 放进 Client |
| C | 一个 `Client.Tests` 覆盖三块 |

**确认**：选推荐 **A**。

### Q14 — 控制台编码 / `--json` / help 语言

**问题**：Windows 代码页、JSON 是否 pretty、help 中英。

| 选项 | 含义 |
|---|---|
| **A** | stdout/stderr UTF-8；`--json` 紧凑；help / `--version` 英文；错误 `--json` stderr：`{"error":{"code":"...","message":"..."}}` |
| B | 不改编码；JSON indented；help 中文 |
| C | UTF-8 + pretty JSON + 中文 help |

**确认**：选推荐 **A**。

---

## C. Round 3 — 实现计划切分

### Q15 — CLI 怎么测

**问题**：是否 `Process.Start(el.exe)`？

| 选项 | 含义 |
|---|---|
| **A** | `ElApp.RunAsync(...)` 进程内调用。假 `IElApi`。`--help`/`--version`/`completion` 不注入 API。不默认拉起进程 |
| B | 每个用例 `dotnet run` 拉起 `el.exe` |
| C | 只测 Client，CLI 靠手工 |

**确认**：选推荐 **A**。

### Q16 — `HttpClient` 与配置加载

**问题**：配置合并与 HttpClient 生命周期。

| 选项 | 含义 |
|---|---|
| **A** | `ElConfig.Load` 在 Client。`HttpElApi` 持有一个 `HttpClient`：`Timeout=30s`，`Accept: application/json`，业务请求 Bearer；`HealthAsync` 不带 Authorization。不引入 `IHttpClientFactory` |
| B | 配置合并只写在 CLI |
| C | `IHttpClientFactory` + Generic Host |

**确认**：选推荐 **A**。

### Q17 — 数字与空 alias 是否本地拦

**问题**：非法 `--limit` 等、trim 后空 alias 是本地还是发给 API？

| 选项 | 含义 |
|---|---|
| **A** | CLI 本地 `bad_request`，不发请求 |
| B | 原样发给 API |
| C | limit 本地拦，空 alias 发给 API |

**确认**：选推荐 **A**。

### Q18 — Ctrl+C

**问题**：取消的退出码。

| 选项 | 含义 |
|---|---|
| **A** | 取消 = `connection_failed`，exit **2** |
| B | 取消 exit 0 |
| C | 取消 exit 1，`bad_request` |

**确认**：选推荐 **A**。

### Q19 — 说明文档

**问题**：是否写 `docs/cli.md`？

| 选项 | 含义 |
|---|---|
| **A** | 短 `docs/cli.md` + `docs/README.md` 一行。规格仍以 CLI 规格为准 |
| B | 不写 docs |
| C | 把 CLI 规格正文抄进 docs |

**确认**：选推荐 **A**。

### Q20 — 实现任务切分

**问题**：计划里几个 Task？

| 选项 | 含义 |
|---|---|
| **A** | 8 个 Task：PathUri → ElResult/ElConfig → HttpElApi → FileTarget → CLI 骨架 → tag/untag/tags → query/preview/suggest → completion+docs。commit 用仓库递增 `#序号`，不要 backlog `#11` |
| B | 两个大 Task：先整个 Client，再整个 CLI |
| C | 按子命令各一个 Task |

**确认**：选推荐 **A**。

### Q21 — Service 对 PathUri 的包装

**问题**：删不删 `UriNormalizer` 类名？

| 选项 | 含义 |
|---|---|
| **A** | 保留 `UriNormalizer.Normalize(uri, kindHint) → ServiceResult<(Uri, Kind)>` 作薄封装。字符串规则只在 PathUri。Service.Tests 留 1–2 个包装测 |
| B | 删 `UriNormalizer`，Resource 代码直接调 PathUri |
| C | PathUri 返回 `ServiceResult` |

**确认**：选推荐 **A**。

---

## 落盘对照

| 主题 | 位置 |
|---|---|
| 交付面 / IElApi / PathUri / SCL / Result / 测 / 补全 | 实现计划 Global Constraints、Task 1–8 |
| 人类输出原文 | 实现计划 Task 6–7 |
| `GetTags` 翻页、DTO 不共享 | Task 3 |
| UTF-8、紧凑 JSON、英文 help | Task 5 |
| 进程内 `ElApp`、config、30s、取消 exit 2 | Task 2、3、5 |
| Service 薄封装 | Task 1 |

写计划时一条收束：Q12 曾写 `SuggestAsync → IReadOnlyList<SuggestItem>`，与 API `data` 分页信封及 `--json` 仅 `data` 冲突。**Ruling：`SuggestAsync` 返回 `SuggestPage`**（见实现计划 Task 7）。若错，代价是改 Client 签名一次。
