# #14 Explorer 上下文菜单包 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 Windows Explorer 稀疏包（`IExplorerCommand`）+ 进程外 WPF `EverythingLabeled.ExplorerHost`，对选中文件/文件夹打标/去标/看标签，并支持按标签 preview 查找；只走 Service HTTP + Bearer。

**Architecture:** Command DLL 无业务/HTTP，只写 `--paths-file` 并以即发即忘方式启动同目录 `ExplorerHost.exe`。硬规则与 UI 全在 Host：读 AppData `config.json`（不写回）、PathUri 与 CLI 同库、经现有 `IElApi`/`HttpElApi` 调 Service。先 Host（命令行可测）后菜单。不强制单实例。

**Tech Stack:** .NET 8；WPF（`net8.0-windows`）；稀疏包 / `IExplorerCommand`；`EverythingLabeled.Client` + `PathUri`；xunit。无 Fluent、无安装器、无 UI 自动化门禁。

**Spec:** [plans/2026-09-27-explorer-integration.md](./2026-09-27-explorer-integration.md)。HTTP：[plans/2026-09-26-http-api.md](./2026-09-26-http-api.md)。CLI/PathUri 基线：[plans/2026-10-06-cli-impl.md](./2026-10-06-cli-impl.md)。审核（实现计划）：待 grill 后写入 `confirmed/2026-10-07-explorer-impl-review.md`。

**Design rulings（brainstorm 已确认，均推荐 A）：**

1. 计划含本地稀疏包注册步骤与手工验收；**不做**正式安装器（#6）。
2. 两项目进现有 sln：`ExplorerCommand` + `ExplorerHost`；Host 引用 Client/PathUri。
3. Host 可测逻辑 xUnit；无 COM/WPF UI 自动化门禁。
4. Host exe 与 Command DLL **同目录**；找不到则提示一次。
5. 开发注册用稀疏包 + 外部 location（`Add-AppxPackage -Register` 或计划钉死的等价命令）。
6. 实现顺序：**先 Host、后菜单**。

## Global Constraints

- TFM：Host / Command / Host.Tests = **`net8.0-windows`**；Client/PathUri 保持 `net8.0`（Host 引用即可）。`<Nullable>enable</Nullable>`；`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。
- 进 `EverythingLabeled.sln`。非 Windows 上编 sln 时这两个项目与 Host.Tests 用条件跳过或文档标明「须 Windows」——**不得**让非 Windows 强制失败整个无条件 sln 的既有项目；推荐：Explorer 项目正常加入，CI/文档说明 `dotnet test` 在非 Windows 跳过 `Explorer*`。
- Command DLL：**不**读 token、**不** HTTP、**不** PathUri 硬规则、**不**画业务 UI；`Invoke` **不等待** Host 退出。
- Host 启动：`--mode tag|untag|tags|find`；有选中时 `--paths-file <path>`。`find` **忽略** paths-file 内容（可省略该参数）。
- `--paths-file`：目录 `%LOCALAPPDATA%\EverythingLabeled\tmp\`；随机文件名；UTF-8；一行一个绝对路径；ACL 尽量仅当前用户；Host **读完即删**；写入前与 Host 启动时清理超过 **60** 分钟的旧文件。
- Host 路径：`Path.Combine(Path.GetDirectoryName(commandDllPath)!, "EverythingLabeled.ExplorerHost.exe")`。
- Config：只读 `%AppData%\EverythingLabeled\config.json`（`Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `\EverythingLabeled\config.json`）。**不写回**。无 `--base-url`/`--token` 覆盖；不用 env 覆盖（与规格「v1 不提供覆盖」一致；加载时对 `ElConfigLoader` 传空 overrides + 空 env）。
- `baseUrl`：先经现有 `ElConfigLoader` 规范化（绝对 http/https、尾 `/`），Host **再**强制 host 为 **`127.0.0.1`**（拒绝 `localhost` / `[::1]` / 其它）；非法或缺 token → 阻断。
- `confirmUntag`：缺省键或省略 = **`true`**（已有 Client 行为）。
- `previewLimit`：缺省 **16**；合法 **1–50**；非法数字 → 当 16。读自同一 config 文件（Client 扩展字段，见 Task 2）。
- `maxConcurrency`：缺省 **4**；合法 **1–16**；越界 → 当 **4**。
- HTTP：整请求超时 **30s**；Bearer（health 除外）；经 `HttpElApi`。
- 启动探测（每次打开）：`GET /v1/health` → 有 token 再 `GET /v1/meta`（需给 `IElApi` 增加 `MetaAsync`）；失败置顶阻断，禁用业务。
- 路径：仅真实文件系统文件/文件夹；`PathUriCanonicalizer.TryCanonicalize`；非法丢弃并计 N；规范化 `file://` **去重**；打标/去标去重后 **>100** → **整次拒绝**（不截断）。看标签仅 **1** 项。`.lnk` **不**解析到目标。
- 打标：`FileTarget.ResolveKindForTag`；不存在/断链预检排除；不传 `displayName`；每次 **一个**别名；`createMissing` 仅勾选（默认关，会话保留）；去标永不 `createMissing: true`。
- 批量：并发 = `maxConcurrency`；可取消（停未发出）；401/缺配置 → 中止剩余；其它错误按项继续；汇总成功/失败/已取消未发送。
- find：仅 `PreviewAsync`；`q` 按 CLI/`ElRequestRules`（trim 后提交）；`limit=previewLimit`；不传 `expand`；打开后窗口保持；单次打开一项。
- UI **简体中文**；文案 SSOT：`ExplorerHost` 内 `Copy`/`UiStrings` 类（禁止散落魔法中文重复多处时不抽）。
- 不做：安装器、关系 CRUD、`.lnk` 目标、虚拟命名空间、托盘、单实例、find 加载更多、服务端多资源批量 API、UI/COM 自动化门禁。
- commit subject：仓库递增 `#序号`，**不要** backlog `#14`；不用 conventional commits；PowerShell here-string 传 `-m`。

---

## File structure

```text
src/EverythingLabeled.Client/
  ElConfig.cs              # + PreviewLimit / MaxConcurrency 解析
  IElApi.cs                # + MetaAsync
  HttpElApi.cs             # + MetaAsync
  ClientDtos.cs            # + MetaData
src/EverythingLabeled.ExplorerHost/
  EverythingLabeled.ExplorerHost.csproj   # net8.0-windows, UseWPF, AssemblyName=EverythingLabeled.ExplorerHost
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs
  HostArgs.cs
  PathsFile.cs
  ExplorerConfig.cs          # 读 AppData + loopback 校验
  SelectionNormalizer.cs
  BatchTagOps.cs
  FindOpen.cs
  Probe.cs
  Copy.cs
  ViewModels/…               # 可选；允许 code-behind 最小可用
src/EverythingLabeled.ExplorerCommand/
  EverythingLabeled.ExplorerCommand.csproj
  ExplorerCommands.cs        # IExplorerCommand 实现（四项或一项多 verb）
  HostLauncher.cs
  PathsFileWriter.cs
  Package/                   # AppxManifest + 稀疏包布局所需
    AppxManifest.xml
tests/EverythingLabeled.ExplorerHost.Tests/
  EverythingLabeled.ExplorerHost.Tests.csproj
  PathsFileTests.cs
  ExplorerConfigTests.cs
  SelectionNormalizerTests.cs
  BatchTagOpsTests.cs
  FindOpenTests.cs
  ProbeTests.cs
docs/explorer.md
docs/README.md
EverythingLabeled.sln
```

---

### Task 1: Host 工程骨架 + 参数 + paths-file

**Files:**

- Create: `src/EverythingLabeled.ExplorerHost/EverythingLabeled.ExplorerHost.csproj`
- Create: `src/EverythingLabeled.ExplorerHost/HostArgs.cs`
- Create: `src/EverythingLabeled.ExplorerHost/PathsFile.cs`
- Create: `src/EverythingLabeled.ExplorerHost/App.xaml`、`App.xaml.cs`（可先空窗体）
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/`（csproj 对齐 Client.Tests；TFM `net8.0-windows`；引用 Host）
- Modify: `EverythingLabeled.sln`（`dotnet sln add` Host + Tests）

**Interfaces:**

- Consumes: 无
- Produces:

```csharp
namespace EverythingLabeled.ExplorerHost;

public enum HostMode { Tag, Untag, Tags, Find }

public sealed record HostLaunchArgs(HostMode Mode, string? PathsFilePath);

public static class HostArgsParser
{
    // argv: --mode tag|untag|tags|find ；可选 --paths-file <path>
    // 缺 mode / 非法 mode → fail（返回 false + message）
    public static bool TryParse(IReadOnlyList<string> argv, out HostLaunchArgs args, out string error);
}

public static class PathsFile
{
    public static string TmpDirectory { get; } // %LOCALAPPDATA%\EverythingLabeled\tmp
    public const int StaleMinutes = 60;

    public static void EnsureTmpDirectory();
    public static void CleanupStale(DateTimeOffset utcNow, TimeSpan maxAge);
    // 读全部行（trim 空行跳过）；成功后删除文件；文件不存在 → 空列表不抛
    public static IReadOnlyList<string> ReadAndDelete(string path);
}
```

- [ ] **Step 1: 写失败测试**

`TryParse(["--mode","tag","--paths-file","C:\\a.txt"])` → Tag + path。`TryParse(["--mode","find"])` → Find、PathsFilePath null。非法 mode → false。`ReadAndDelete`：写临时文件两行路径 → 返回两行且文件已删。`CleanupStale`：造 mtime 旧文件 → 删除；新文件保留。

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test tests/EverythingLabeled.ExplorerHost.Tests --filter FullyQualifiedName~HostArgs|FullyQualifiedName~PathsFile`

- [ ] **Step 3: 最小实现** + 空 WPF 窗体能 `dotnet run --project …ExplorerHost -- --mode find`

- [ ] **Step 4: 测过**

- [ ] **Step 5: Commit**

```powershell
git add src/EverythingLabeled.ExplorerHost tests/EverythingLabeled.ExplorerHost.Tests EverythingLabeled.sln
git commit -m @"
#N ExplorerHost 骨架与 paths-file
"@
```

---

### Task 2: Client 扩展 config/meta + Host 探测

**Files:**

- Modify: `src/EverythingLabeled.Client/ElConfig.cs`（`ElConfig` 增加 `PreviewLimit`/`MaxConcurrency`；`ReadFile` 解析；缺省 16 / 4；非法 previewLimit → 16；maxConcurrency 越界 → 4）
- Modify: `src/EverythingLabeled.Client/IElApi.cs`、`HttpElApi.cs`、`ClientDtos.cs`（`MetaData(int DataVersion)` + `MetaAsync` → `GET v1/meta`，authorize true）
- Modify: `tests/EverythingLabeled.Client.Tests/ElConfigTests.cs` + 新/扩 `HttpElApi` 测（若已有假 handler 模式则跟）
- Create: `src/EverythingLabeled.ExplorerHost/ExplorerConfig.cs`、`Probe.cs`、`Copy.cs`
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/ExplorerConfigTests.cs`、`ProbeTests.cs`

**Interfaces:**

```csharp
namespace EverythingLabeled.Client;

public sealed record ElConfig(
    string BaseUrl,
    string Token,
    bool ConfirmUntag,
    int PreviewLimit,
    int MaxConcurrency);

public sealed record MetaData(int DataVersion);

public interface IElApi
{
    // …existing…
    Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default);
}

namespace EverythingLabeled.ExplorerHost;

public sealed record ExplorerRuntimeConfig(
    string BaseUrl,
    string Token,
    bool ConfirmUntag,
    int PreviewLimit,
    int MaxConcurrency);

public static class ExplorerConfigLoader
{
    // 空 overrides + 空 env + AppData 默认路径 → ElConfigLoader.Load
    // 成功后 RequireLoopback(BaseUrl)：Uri.Host 必须等于 "127.0.0.1"（OrdinalIgnoreCase）
    // 失败文案中文，指向配置路径
    public static ElResult<ExplorerRuntimeConfig> Load();
    public static ElResult<ExplorerRuntimeConfig> LoadFrom(ElResult<ElConfig> loaded, string configPathForMessage);
}

public static class Probe
{
    // health 失败 → Fail；再 meta 失败 → Fail（token 无效）
    public static Task<ElResult<Unit>> RunAsync(IElApi api, CancellationToken ct);
}
```

CLI/MCP 现有 `ElConfig` 构造调用处：补默认 `PreviewLimit: 16`、`MaxConcurrency: 4`（或改 `ElConfigLoader` 唯一出口，调用方只拿 record）。**禁止**第二套 JSON 解析复制粘贴；只扩展 `ReadFile`。

- [ ] **Step 1: Client 测** — 文件含 previewLimit 8 → 8；`"x"` → 16；maxConcurrency 99 → 4；0 → 4。MetaAsync 用假 HttpMessageHandler 返回 ok 信封。
- [ ] **Step 2: Host 测** — `http://127.0.0.1:5123/` ok；`http://localhost:5123/` fail；Probe：health fail / meta fail / 双成功。
- [ ] **Step 3: 实现**
- [ ] **Step 4: `dotnet test` Client.Tests + Cli.Tests + Mcp*.Tests + ExplorerHost.Tests**（回归）
- [ ] **Step 5: Commit**

---

### Task 3: 选中规范化（丢弃 / 去重 / ≤100 / 打标预检）

**Files:**

- Create: `src/EverythingLabeled.ExplorerHost/SelectionNormalizer.cs`
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/SelectionNormalizerTests.cs`

**Interfaces:**

```csharp
public sealed record SelectionItem(
    string RawPath,
    string? CanonicalUri,   // file:// …
    string? Kind,           // file|folder；仅打标预检成功时
    bool Included,
    string? ExcludeReason);

public sealed record SelectionResult(
    IReadOnlyList<SelectionItem> Items,
    int IgnoredUnsupportedCount,
    int DedupedCount,
    bool RejectedOverLimit); // true → 调用方整次拒绝，不提交

public static class SelectionNormalizer
{
    public const int MaxItems = 100;

    // mode=Tag 时做存在性/kind（FileTarget.ResolveKindForTag）；Untag/Tags 只 canonicalize
    // 无法 canonicalize → 计入 IgnoredUnsupported，不进 Included
    // 按 CanonicalUri Ordinal 去重（先到保留）
    // DedupedCount > 100 → RejectedOverLimit=true，Items 仍可给 UI 展示
    public static SelectionResult Normalize(IEnumerable<string> rawPaths, HostMode mode);
}
```

- [ ] **Step 1–4: TDD** — 非法路径 ignored；两相同路径 dedupe=1；101 合法 → RejectedOverLimit；Tag 模式下不存在文件 Included=false 带原因；Untag 不存在仍可 Included（有 canonical）。
- [ ] **Step 5: Commit**

---

### Task 4: 批量打标 / 去标编排

**Files:**

- Create: `src/EverythingLabeled.ExplorerHost/BatchTagOps.cs`
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/BatchTagOpsTests.cs`

**Interfaces:**

```csharp
public sealed record BatchItemResult(string Uri, bool Ok, string? ErrorCode, string? ErrorMessage, bool CancelledBeforeSend);

public sealed record BatchSummary(
    int Success,
    int Failed,
    int CancelledNotSent,
    IReadOnlyList<BatchItemResult> Items);

public static class BatchTagOps
{
    public static Task<BatchSummary> TagAsync(
        IElApi api,
        IReadOnlyList<(string Uri, string Kind)> targets,
        string alias,
        bool createMissing,
        int maxConcurrency,
        CancellationToken ct);

    public static Task<BatchSummary> UntagAsync(
        IElApi api,
        IReadOnlyList<string> uris,
        string alias,
        int maxConcurrency,
        CancellationToken ct);
}
```

规则：空 alias（trim 后）→ 调用方不进此方法。每资源一次 `TagAsync`/`UntagAsync`，aliases 单元素列表。并发用 `SemaphoreSlim(maxConcurrency)`。`ct` 取消 → 未启动的标 `CancelledBeforeSend`；已发出的等完成计入成败。任一结果 HTTP 401（或 `ElError` 鉴权码）→ **取消剩余未发出**。其它错误按项继续。

- [ ] **Step 1–4: TDD** — 假 `IElApi` 延迟+计数并发 ≤ maxConcurrency；取消中途；401 后不再发后续；3 成功 1 业务失败汇总。
- [ ] **Step 5: Commit**

---

### Task 5: 看标签 + find 打开

**Files:**

- Create: `src/EverythingLabeled.ExplorerHost/FindOpen.cs`
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/FindOpenTests.cs`
- （看标签直接 `IElApi.GetTagsAsync`；`resource_not_found` → 空列表，Host UI 层映射）

**Interfaces:**

```csharp
public enum OpenKind { SelectInExplorer, OpenFolder, OpenUrl, Unsupported }

public sealed record OpenAction(OpenKind Kind, string Argument);

public static class FindOpen
{
    // file + kind file → SelectInExplorer + 本地路径
    // file + kind folder → OpenFolder
    // url + http(s) → OpenUrl
    // 其它 → Unsupported
    public static OpenAction Decide(string uri, string kind);

    // 实际执行：Process.Start（可注入 Action 测 Decide；执行可 Internals 测或手工）
    public static void Execute(OpenAction action);
}
```

- [ ] **Step 1–4: TDD Decide** 四分支；GetTags `resource_not_found` 在 UI/薄包装测里断言空列表。
- [ ] **Step 5: Commit**

---

### Task 6: WPF 最小可用 UI（四 mode）

**Files:**

- Create/Modify: `MainWindow.xaml` / `.cs` 及必要 ViewModel
- Modify: `App.xaml.cs` — 解析 args → Load config → Probe → Normalize（非 find）→ 显示对应面板

**行为（对齐规格 §5.3–5.5）：**

| mode | UI |
|---|---|
| tag | 列表（含排除说明）；别名输入 + suggest 250ms 防抖 limit 10；勾选创建缺失；提交走 BatchTagOps；进度/取消/汇总；成功保持窗口 |
| untag | 单别名；单选可点 tags 列表删除或手输；多选手输；`confirmUntag` → 确认框含路径数；BatchTagOps.Untag |
| tags | 单路径列表；可切 tag/untag |
| find | q + preview；打开走 FindOpen；保持窗口 |

探测失败 / config 失败：置顶中文说明，业务控件禁用。`>100`：提示「最多 100 项」，不发请求。忽略 N 项：顶部提示。

手工：`dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode tag --paths-file …`（Service 已起、config 已填）。

- [ ] **Step 1: 实现 UI 接线**（逻辑已在前序任务；本任务以手工验收为主，可为 suggest 防抖抽纯函数单测）
- [ ] **Step 2: 手工点通四 mode 主路径**
- [ ] **Step 3: Commit**

```powershell
git commit -m @"
#N ExplorerHost 四 mode WPF 壳
"@
```

---

### Task 7: ExplorerCommand 稀疏包 + 启 Host

**Files:**

- Create: `src/EverythingLabeled.ExplorerCommand/`（csproj、`HostLauncher`、`PathsFileWriter`、`IExplorerCommand` 实现、`Package/AppxManifest.xml`）
- Modify: sln；Host 构建后复制 exe 到 Command 输出目录（`csproj` Target 或文档约定 `dotnet publish` 布局）
- Create: 开发注册脚本或 `docs/explorer.md` 内嵌步骤（本任务至少有可执行步骤草稿）

**Interfaces:**

```csharp
internal static class HostLauncher
{
    public static void StartFireAndForget(HostMode mode, string? pathsFilePath);
}

internal static class PathsFileWriter
{
    // CleanupStale → 写 UTF-8 行 → 返回 path；ACL 尽量仅当前用户
    public static string Write(IEnumerable<string> absolutePaths);
}
```

菜单（简体中文）：打标 / 去标 / 看标签 / 按标签查找。子菜单标题 `EverythingLabeled`。空白处仅「按标签查找」。看标签：选中 ≠1 → 禁用。打标/去标：**不**因数量灰掉。Invoke：写 paths-file（若需要）→ StartFireAndForget。

稀疏包：按 Microsoft 稀疏包 + Explorer 命令文档布局；开发机：

```powershell
Add-AppxPackage -Register .\AppxManifest.xml
# 卸载用 Remove-AppxPackage（包全名以 Manifest 为准）
```

（若实测命令需微调，以 `docs/explorer.md` 最终钉死为准，本任务实现可跑通的一种。）

- [ ] **Step 1: 实现 DLL + Manifest + 同目录 Host 复制**
- [ ] **Step 2: 手工注册 → 右键四项 → Host 弹出且不卡 Explorer**
- [ ] **Step 3: Commit**

---

### Task 8: 实现说明 + 索引

**Files:**

- Create: `docs/explorer.md`
- Modify: `docs/README.md`（表格加 #14）
- Modify: `plans/2026-09-27-explorer-integration.md` 仅当实现跟踪句需要链到本计划时（已有规格则加「实现计划」链接一行，避免改行为）

`docs/explorer.md` 写清：

- 工程：`ExplorerHost` / `ExplorerCommand`；`dotnet build` / `dotnet test tests/EverythingLabeled.ExplorerHost.Tests`
- 命令行启动 Host 示例
- 稀疏包注册/卸载步骤
- 依赖 Service + AppData config（含 previewLimit / maxConcurrency）
- 手工验收对齐规格 §10 清单

- [ ] **Step 1: 写文档**
- [ ] **Step 2: Commit**

```powershell
git add docs
git commit -m @"
#N Explorer 加载与注册说明
"@
```

---

## Self-review

1. **Spec coverage:** §2–3 菜单/选中 → T7 + T3；§4 PathUri → T3；§5 config/探测/面板/API/打开 → T2/T4/T5/T6；§6 错误空态 → T3/T4/T6；§7 安装边界 → 仅开发注册（ruling）；§9 非目标未列入；§10 → T8 清单。
2. **Placeholders:** 无 TBD；稀疏包具体 XML 属性在 T7 按微软模板填写，不得留空包名。
3. **Types:** `HostMode` / `ElConfig` 扩展 / `MetaAsync` / `BatchTagOps` 在后续任务与 T1–4 一致。
4. **Client 回归:** T2 必须跑 Cli + Mcp 测试，因 `ElConfig` 形状变化。
