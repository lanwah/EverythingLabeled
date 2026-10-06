# CLI（#11）实现计划讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 1–5  
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

## D. Round 4 — `--json` 保真、签名冻结、HTTP 拼接

对已落盘实现计划的第二轮 grill（Q1–Q21 不变）。事实：`HealthAsync` 对应现网 **`GET /v1/health`**，无 Bearer。离线 `--help` / `--version` / `completion powershell` 忽略 `--json`（规格里 `--json` 只约束 API `data`）。

### Q22 — `--json` 要不要跟 API `data` 字段集一致

**问题**：规格写成功 stdout 仅 API `data`。Service `TagDto` / `ResourceDetailDto` / `TagApplyResultDto` 含 `namespace`、`seq`、`nameInitials`、时间戳、`revision`。计划里的瘦 `TagInfo` 只有 `id`/`name`/`aliases`。人类输出仍只用别名/`Name`。

| 选项 | 含义 |
|---|---|
| **A** | Client DTO 对齐 Service 同名形状（`TagInfo`=`TagDto` 字段；`ResourceDetail` 含 `createdAt`/`updatedAt`/`revision`/`tags`；`createdTags` 用完整 Tag）。JSON：`JsonSerializerDefaults.Web` + **不省略 null**（对齐 `ApiEnvelope` 的 `DefaultIgnoreCondition.Never`）。人类格式仍按 Q8–Q10。仍不建 Contracts 工程、不引用 Service |
| B | 维持计划子集；`--json` 允许比 HTTP 瘦 |
| C | 本轮引用 Service DTO（打破「Client.Tests 不引用 Service」） |

**确认**：选推荐 **A**。

### Q23 — `IElApi` / DTO 在哪一 Task 钉死

**问题**：计划 Task 3 的 `TagApplyData` 无 `resource`、`GetTagsAsync`/`SuggestAsync` 返回扁平列表；Task 6/7 再改签名。

| 选项 | 含义 |
|---|---|
| **A** | **Task 3 一次写最终形状**：`TagApplyData` 含 `Resource`；`GetTagsAsync` → `TagsPage(items, offset:0, limit:count, total:count)`（翻页拼完后的合成 `data`）；`SuggestAsync` → `SuggestPage`。Task 6/7 **不得**再改 `IElApi` 签名 |
| B | 保持原计划：Task 3 先瘦实现，6/7 再改签名 |
| C | `GetTagsAsync` 仍返回列表，只由 CLI 包一层 JSON |

**确认**：选推荐 **A**。

### Q24 — `baseUrl` 怎么拼路径

**问题**：`HttpClient.BaseAddress` 有无尾 `/`、请求是 `/v1/...` 还是 `v1/...`，会拼错 URL。

| 选项 | 含义 |
|---|---|
| **A** | trim `baseUrl`，**保证恰好一个尾 `/`**；请求用相对路径 **`v1/...`（无前导 `/`）**。测试钉有/无尾斜杠两种输入最终请求 URI 相同 |
| B | 调用方原样传入；请求一律 `/v1/...` |
| C | 每次用绝对 `new Uri(baseUrl + "/v1/...")` 手拼 |

**确认**：选推荐 **A**。

### Q25 — `HttpClient` 寿命

**问题**：`ElApp` 在 `Api==null` 时现造 client。进程内测试注入假 API。

| 选项 | 含义 |
|---|---|
| **A** | 每次 `RunAsync` 需要网络时 `new HttpClient`，`using` 掉；不设静态单例。不引入 `IHttpClientFactory` |
| B | 静态共用一个 `HttpClient`（改 token 靠改 DefaultHeaders） |
| C | Generic Host + Factory |

**确认**：选推荐 **A**。

### Q26 — `el --version` 打什么

**问题**：计划写 InformationalVersion；csproj 未定 Version，空字符串会让测试发虚。

| 选项 | 含义 |
|---|---|
| **A** | Cli csproj 设 `<Version>0.1.0</Version>`。`--version` stdout 一行含 `0.1.0`，stderr 空，exit 0 |
| B | 硬编码 `"el"`，不绑 SemVer |
| C | 打 git commit hash |

**确认**：选推荐 **A**。

### Q27 — SCL 解析失败时的 `--json`

**问题**：未知选项、缺位置参数等，SCL 会自己打英文用法；计划的 `bad_request` JSON 管不到解析器。

| 选项 | 含义 |
|---|---|
| **A** | handler 前能拦的走 `WriteError`。SCL 解析失败：人类模式保留 SCL 英文 stderr，exit **1**；若已出现 `--json`，stderr 额外或改为标准 error JSON，stdout 空。不把整份 help 塞进 JSON |
| B | 全部改写 SCL，任何解析失败都只输出我们的 error，永远不出现 SCL help |
| C | 解析失败不管：SCL 默认输出 + 非 0，`--json` 也可以没有 error 对象 |

**确认**：选推荐 **A**。（Q29 把「额外或改为」收束为 **只** JSON。）

### Q28 — 补全脚本运行时怎么调 `suggest`

**问题**：计划写 `--json` 或解析人类 TSV。

| 选项 | 含义 |
|---|---|
| **A** | 脚本只调 `el suggest --json -- <prefix>`，解析 `items[].alias`；非 0 / 非 JSON → 空补全。不解析人类 TSV。打印脚本本身仍离线、不读 config |
| B | 调人类模式 `el suggest <prefix>`，按 tab 切列 |
| C | 脚本内嵌 `Invoke-RestMethod` 打 API |

**确认**：选推荐 **A**。

---

## E. Round 5 — 解析失败 JSON、非法 url、重复 alias、reparse、`ElJson`

### Q29 — `--json` 遇上 SCL 解析失败，stderr 能不能混 SCL help

**问题**：Q27 允许「额外或改为」。脚本若解析 stderr 为 JSON，前面再跟用法文本会失败。

| 选项 | 含义 |
|---|---|
| **A** | 命令行已出现 `--json`：stderr **只**打标准 error JSON，**不要** SCL help；stdout 空；exit 1。人类模式仍用 SCL 英文用法 |
| B | `--json` 时 stderr 先 SCL help 再跟一行 JSON |
| C | `--json` 时把 help 塞进 `error.message` |

**确认**：选推荐 **A**。

### Q30 — 合并后的 `baseUrl` 不是绝对 http(s)

**问题**：例如 `localhost:5123`、`file:///...`。规格只写了缺文件/缺必填。

| 选项 | 含义 |
|---|---|
| **A** | trim 后必须是绝对 URI 且 scheme 为 `http`/`https`。否则 `config_missing`，exit **2**，不发请求。`token` 只要求非空白 |
| B | 非法 url → 本地 `bad_request`，exit 1 |
| C | 原样交给 `HttpClient`，失败再算 `connection_failed` |

**确认**：选推荐 **A**。

### Q31 — 一次 `tag`/`untag` 里重复 alias

**问题**：如 `el tag f.txt 工作 工作`。规格没写本地去重。

| 选项 | 含义 |
|---|---|
| **A** | 只 trim；空段仍 `bad_request`。**不去重、不改大小写**，按用户顺序发给 API |
| B | 本地按规范化别名去重（保序）再发送 |
| C | 发现重复 → 本地 `bad_request` |

**确认**：选推荐 **A**。

### Q32 — junction / 非 symlink 的 reparse

**问题**：计划只写 symlink + `ResolveLinkTarget`。Windows 上目录 junction 常见；无权限时 symlink 测会 skip。

| 选项 | 含义 |
|---|---|
| **A** | 身份仍是用户给出的路径/URI。存在性/`kind`：`ResolveLinkTarget(..., returnFinalTarget: true)`；null 则回退 `File.Exists` / `Directory.Exists`。断链 → `path_not_found`。测试：symlink 无权限则 skip；不强制测 junction |
| B | 只处理明确是 symlink 的 reparse；junction 当普通目录，不解析目标 |
| C | 任何 reparse 一律 `path_not_found` |

**确认**：选推荐 **A**。

### Q33 — JSON 选项放哪

**问题**：Q22 要求与 `ApiEnvelope` 同策略。HttpElApi 反序列化与 CLI `--json` 序列化若各写一份会分叉。

| 选项 | 含义 |
|---|---|
| **A** | Client 公开 `ElJson.Options`（`JsonSerializerDefaults.Web` + `DefaultIgnoreCondition.Never`）。`HttpElApi` 与 CLI `--json` **只准用这一份**。不引用 Service |
| B | Client 与 CLI 各 new 一份相同选项 |
| C | 直接默认 `JsonSerializer.Serialize` |

**确认**：选推荐 **A**。

---

## 落盘对照

| 主题 | 位置 |
|---|---|
| 交付面 / IElApi / PathUri / SCL / Result / 测 / 补全 | 实现计划 Global Constraints、Task 1–8 |
| 人类输出原文 | 实现计划 Task 6–7 |
| `GetTags` 翻页、DTO 不共享、字段对齐 Service、`ElJson`、`SuggestPage`/`TagsPage` | Task 2–3（Q22–Q23、Q33） |
| UTF-8、紧凑 JSON、英文 help、`0.1.0`、SCL `--json` 纯 error | Task 5（Q26、Q27、Q29） |
| 进程内 `ElApp`、config、30s、取消 exit 2、HttpClient `using`、baseUrl | Task 2、3、5（Q24–Q25、Q30） |
| Service 薄封装 | Task 1 |
| FileTarget reparse | Task 4（Q32） |
| 重复 alias、completion `suggest --json` | Task 6、8（Q31、Q28） |

Q12 曾写 `SuggestAsync` / `GetTagsAsync` 返回 list。**Q23：Task 3 即 `SuggestPage` / `TagsPage` / 带 `resource` 的 `TagApplyData`**，后续 Task 不改签名。
