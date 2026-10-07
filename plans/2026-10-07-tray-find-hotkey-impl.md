# Tray Find 热键 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 WinForms `EverythingLabeled.Tray`（全局热键 + 托盘 + HKCU 自启）与 Find 全入口单实例（Mutex + 命名管道 Activate），按规格唤起/前置同目录 `ExplorerHost --mode find`。

**Architecture:** Tray 只做热键/托盘/自启/IPC 客户端，不读 token、不做业务 HTTP。Find 单实例逻辑进 Host：`--mode find` 抢 Mutex，赢家开窗并监听管道，输家发 `Activate\n` 后退出。管道客户端与名称解析放 `ExplorerShared`，供 Tray 与 Host 共用。Tray 构建后把 Host **整输出目录**拷到 Tray 旁（与 Command 同约定）。

**Tech Stack:** .NET 8；WinForms（`net8.0-windows`，**x64**）；WPF Host（既有）；Win32 `RegisterHotKey` / Named Pipe / Named Mutex；`System.Text.Json` `JsonObject` 读-改-写；xunit。无设置 UI、无 Tray 文件日志、无社区 MVVM、无 UI 自动化门禁。

**Spec:** [plans/2026-10-07-tray-find-hotkey.md](./2026-10-07-tray-find-hotkey.md)。审核：[confirmed/2026-10-07-tray-find-hotkey-review.md](./confirmed/2026-10-07-tray-find-hotkey-review.md)。配置：[plans/2026-09-27-config-and-install.md](./2026-09-27-config-and-install.md)。Explorer 基线：[plans/2026-10-07-explorer-impl.md](./2026-10-07-explorer-impl.md)。

## Global Constraints

- TFM：Tray / Tray.Tests / Host / Shared = **`net8.0-windows`**；**PlatformTarget x64**；`<Nullable>enable</Nullable>`；`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。
- 进 `EverythingLabeled.sln`（`dotnet sln add`）。
- Tray **单实例**：默认 Mutex `Local\EverythingLabeled.Tray`；第二实例**静默退出**；测试覆盖名 `EL_TRAY_MUTEX_NAME`。
- Find **单实例**（仅 `--mode find`）：默认 Mutex `Local\EverythingLabeled.FindHost`；命名管道默认名 `EverythingLabeled.FindHost`（`\\.\pipe\...`）；测试覆盖 `EL_FIND_MUTEX_NAME` / `EL_FIND_PIPE_NAME`。`tag`/`untag`/`tags` **不**抢 Find Mutex。
- 管道协议：单行 UTF-8 明文 **`Activate\n`**（含换行）；其它行忽略。
- Host 路径：Tray 仅认 `Path.Combine(TrayDir, "EverythingLabeled.ExplorerHost.exe")`；**无** `EL_HOST_PATH`。
- 唤起路径：先 `TrySendActivate`；失败再 `Process.Start` 同目录 Host `--mode find`。热键/菜单/单击约 **300ms** 防抖。
- 前置：若最小化则 Restore，再尽力 `Activate`/`SetForegroundWindow`；失败任务栏闪烁可接受，不视为缺陷。
- 关 Find 窗 = 释放 Mutex + 停管道；下次热键**新开** Host。Tray **退出不杀** Host。
- `findHotkey`：默认 `"Alt+L"`；空串或规范化 `none` → 不注册；非法非空 → 回退 `Alt+L`；改 JSON 须**重启 Tray**。主键仅 `A–Z`/`0–9`/`F1–F12`/`Space`；修饰 `Ctrl`/`Alt`/`Shift`/`Win`，`+` 连接，大小写不敏感，段间可空白。
- `runAtStartup` 默认 `true`；HKCU Run 值名 **`EverythingLabeled.Tray`** → Tray 绝对路径；与 Service 的 `EverythingLabeled` **并存**。
- Config：`%AppData%\EverythingLabeled\config.json`（尊重 `EL_APPDATA` 根，与 Service `AppPaths` 一致）。Tray 可读缺文件（用默认）；写 `runAtStartup` 须 **JsonObject 保留未知键**，原子写（tmp + replace），失败短重试 1–2 次。Service `WriteAtomic`：**不**主动写出新键默认；若目标文件已存在则合并保留未知键。
- Tray **不依赖** `baseUrl`/`token` 即可挂托盘；**无**文件日志；失败用气泡/短提示（简体中文，文案 SSOT `Copy`）。
- 可测逻辑 xUnit；无 WinForms/WPF UI 自动化门禁；Find 单实例测用可注入名称 + 短超时管道，不启真实托盘。
- commit subject：仓库递增 `#序号`；不用 conventional commits；PowerShell here-string 传 `-m`。

---

## File structure

```text
src/EverythingLabeled.ExplorerShared/
  FindHostIpc.cs              # 名称解析 + TrySendActivate + ActivateLine 常量
src/EverythingLabeled.ExplorerHost/
  FindSingleInstance.cs       # 抢 Mutex、管道服务端、Dispose 释放
  App.xaml.cs                 # find 模式接入；输家 Activate 后 Shutdown
  MainWindow.xaml.cs          # 提供 ActivateFromIpc（Restore + 尽力前置）
src/EverythingLabeled.Tray/
  EverythingLabeled.Tray.csproj   # WinExe, UseWindowsForms, x64, Copy Host 输出
  Program.cs
  TrayApplicationContext.cs
  HotkeyParser.cs
  HotkeyRegistration.cs
  TrayConfigStore.cs
  FindLauncher.cs
  StartupRun.cs
  NativeMethods.cs
  Copy.cs
  Assets/tray.ico             # 简单托盘图标（可嵌入资源）
tests/EverythingLabeled.Tray.Tests/
  HotkeyParserTests.cs
  TrayConfigStoreTests.cs
  FindLauncherTests.cs        # 可选：mock/管道；以可测纯函数为主
tests/EverythingLabeled.ExplorerHost.Tests/
  FindHostIpcTests.cs
  FindSingleInstanceTests.cs
src/EverythingLabeled.Service/Hosting/
  ConfigBootstrap.cs          # WriteAtomic 已存在文件时保留未知键
tests/EverythingLabeled.Service.Tests/
  ConfigBootstrapPreserveTests.cs
docs/explorer.md
docs/README.md
EverythingLabeled.sln
```

---

### Task 1: Find IPC（Shared）+ Host 单实例

**Files:**

- Create: `src/EverythingLabeled.ExplorerShared/FindHostIpc.cs`
- Create: `src/EverythingLabeled.ExplorerHost/FindSingleInstance.cs`
- Modify: `src/EverythingLabeled.ExplorerHost/App.xaml.cs`
- Modify: `src/EverythingLabeled.ExplorerHost/MainWindow.xaml.cs`（公开 `ActivateFromIpc()`）
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/FindHostIpcTests.cs`
- Create: `tests/EverythingLabeled.ExplorerHost.Tests/FindSingleInstanceTests.cs`

**Interfaces:**

- Consumes: 现有 `HostArgsParser` / `HostMode.Find` / `MainWindow`
- Produces:

```csharp
namespace EverythingLabeled.ExplorerShared;

public static class FindHostIpc
{
    public const string DefaultMutexName = @"Local\EverythingLabeled.FindHost";
    public const string DefaultPipeName = "EverythingLabeled.FindHost";
    public const string ActivateLine = "Activate\n"; // 写入管道时含 '\n'

    public static string ResolveMutexName(IReadOnlyDictionary<string, string?>? env = null);
    // EL_FIND_MUTEX_NAME 非空白则用之，否则 DefaultMutexName

    public static string ResolvePipeName(IReadOnlyDictionary<string, string?>? env = null);
    // EL_FIND_PIPE_NAME 非空白则用之，否则 DefaultPipeName

    /// <summary>连接命名管道并写 ActivateLine；成功 true。超时/失败 false。</summary>
    public static bool TrySendActivate(string pipeName, TimeSpan timeout);
}

namespace EverythingLabeled.ExplorerHost;

public sealed class FindSingleInstance : IDisposable
{
    public static bool TryAcquire(
        string mutexName,
        string pipeName,
        Action onActivate,
        out FindSingleInstance? instance);
    // 抢到 Mutex → 起管道循环（后台线程），收到 ActivateLine.TrimEnd()=="Activate" 则 SynchronizationContext/Dispatcher 调 onActivate
    // 未抢到 → instance=null，返回 false
}
```

- [ ] **Step 1: 写失败测试**

`ResolveMutexName`/`ResolvePipeName`：无 env → 默认；有覆盖 → 覆盖值。

`TrySendActivate`：无服务端 → false（短超时 ~200ms）。

`TryAcquire`：同名 Mutex 第一次 true；第二次 false；第一次 Dispose 后第三次可再抢到。管道：Acquired 后 `TrySendActivate` true 且 `onActivate` 被调用至少一次（用 ManualResetEventSlim）。

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test tests/EverythingLabeled.ExplorerHost.Tests --filter FullyQualifiedName~FindHostIpc|FullyQualifiedName~FindSingleInstance`

- [ ] **Step 3: 最小实现**

`FindHostIpc`：`NamedPipeClientStream` + `StreamWriter` 写 `Activate`+换行。

`FindSingleInstance`：`Mutex(initiallyOwned:false, name, out createdNew)`；仅 `createdNew` 时拥有。管道：`NamedPipeServerStream(pipeName, In, 1, Byte, Asynchronous)` 循环 WaitForConnection → 读一行 → 断连 → 再等；`Dispose` 释放 Mutex、取消循环、关管道。

`App.OnStartup`：若 `Mode==Find`，在建窗前 `TryAcquire`；失败则 `FindHostIpc.TrySendActivate`（超时 ~1s）后 `Shutdown(0)` 且 **不** `Show`；成功则把 `instance` 挂到 App 字段，`onActivate` → `window.ActivateFromIpc()`（窗体创建后赋值）。`OnExit`/`window.Closed` Dispose instance。

`MainWindow.ActivateFromIpc()`：`Dispatcher.Invoke`：若 `WindowState==Minimized` → `Normal`；`Show()`；`Activate()`；可选短暂 `Topmost` 翻转；尽力 `SetForegroundWindow`（P/Invoke，失败忽略）。

- [ ] **Step 4: 测过**

同上 filter，期望 PASS。另：非 find 模式启动路径不变（现有 HostArgs 测仍绿）。

- [ ] **Step 5: Commit**

```powershell
git add src/EverythingLabeled.ExplorerShared/FindHostIpc.cs src/EverythingLabeled.ExplorerHost/FindSingleInstance.cs src/EverythingLabeled.ExplorerHost/App.xaml.cs src/EverythingLabeled.ExplorerHost/MainWindow.xaml.cs tests/EverythingLabeled.ExplorerHost.Tests/FindHostIpcTests.cs tests/EverythingLabeled.ExplorerHost.Tests/FindSingleInstanceTests.cs
git commit -m @"
#N Find 单实例 Mutex+管道 Activate

"
```

---

### Task 2: HotkeyParser + TrayConfigStore

**Files:**

- Create: `src/EverythingLabeled.Tray/EverythingLabeled.Tray.csproj`（先可无 UI：`UseWindowsForms`，WinExe，x64，`AssemblyName=EverythingLabeled.Tray`）
- Create: `src/EverythingLabeled.Tray/HotkeyParser.cs`
- Create: `src/EverythingLabeled.Tray/TrayConfigStore.cs`
- Create: `src/EverythingLabeled.Tray/Copy.cs`（本 Task 可先只放与配置无关的占位；或仅测用不依赖 Copy）
- Create: `tests/EverythingLabeled.Tray.Tests/EverythingLabeled.Tray.Tests.csproj`（对齐 ExplorerHost.Tests；引用 Tray）
- Create: `tests/EverythingLabeled.Tray.Tests/HotkeyParserTests.cs`
- Create: `tests/EverythingLabeled.Tray.Tests/TrayConfigStoreTests.cs`
- Modify: `EverythingLabeled.sln`（`dotnet sln add` Tray + Tray.Tests）

**Interfaces:**

- Consumes: 无（纯逻辑）
- Produces:

```csharp
namespace EverythingLabeled.Tray;

public enum HotkeyParseKind { Register, Disabled, FallbackDefault }

public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const string DefaultChord = "Alt+L";
    // Win32: MOD_ALT=0x0001, MOD_CONTROL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008
    // VK: 'A'..'Z', '0'..'9', VK_F1=0x70..VK_F12=0x7B, VK_SPACE=0x20

    /// <summary>
    /// 空/空白或 none → Disabled。
    /// 合法 → Register + Spec。
    /// 非法非空 → FallbackDefault + Alt+L 的 Spec。
    /// </summary>
    public static HotkeyParseKind Parse(string? raw, out HotkeySpec spec);
}

public sealed record TrayConfig(string FindHotkey, bool RunAtStartup);

public static class TrayConfigStore
{
    public const string DefaultFindHotkey = "Alt+L";
    public const bool DefaultRunAtStartup = true;

    public static string DefaultConfigPath(); // EL_APPDATA 或 %AppData%\EverythingLabeled\config.json

    public static TrayConfig Load(string configPath);
    // 缺文件/坏 JSON → 默认；缺键用默认；findHotkey 原样字符串读出（解析留给 HotkeyParser）

    public static bool TrySetRunAtStartup(string configPath, bool value, int retries = 2);
    // JsonObject 读-改-写保留未知键；原子写；失败重试
}
```

- [ ] **Step 1: 写失败测试**

Hotkey：`"Alt+L"` / `"alt + l"` → Register，MOD_ALT + VK L。`"Ctrl+Shift+F12"` → Register。`""` / `"none"` / `" None "` → Disabled。`"Alt+Left"` / `"Ctrl+Oem1"` / `"Alt"`（无主键）→ FallbackDefault（Spec 等于 Alt+L）。`"Space"` 单独非法（无修饰）→ FallbackDefault；`"Ctrl+Space"` → Register。

Config：缺文件 → 默认。JSON 含 `findHotkey:"Ctrl+K"`, `runAtStartup:false`, `token:"abc"`, `extra:1` → Load 读出 Ctrl+K/false；`TrySetRunAtStartup(path,true)` 后文件仍含 `token`/`extra`，且 `runAtStartup:true`。

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test tests/EverythingLabeled.Tray.Tests --filter FullyQualifiedName~HotkeyParser|FullyQualifiedName~TrayConfigStore`

- [ ] **Step 3: 最小实现**（Program 可先空 `Main` 立即 return，保证工程可编）

- [ ] **Step 4: 测过**

- [ ] **Step 5: Commit**

```powershell
git add src/EverythingLabeled.Tray tests/EverythingLabeled.Tray.Tests EverythingLabeled.sln
git commit -m @"
#N Tray 热键解析与配置读写

"
```

---

### Task 3: Tray 进程（单实例、热键、托盘、自启、唤起）

**Files:**

- Create/Modify: `src/EverythingLabeled.Tray/Program.cs`
- Create: `src/EverythingLabeled.Tray/TrayApplicationContext.cs`
- Create: `src/EverythingLabeled.Tray/HotkeyRegistration.cs`
- Create: `src/EverythingLabeled.Tray/FindLauncher.cs`
- Create: `src/EverythingLabeled.Tray/StartupRun.cs`
- Create: `src/EverythingLabeled.Tray/NativeMethods.cs`
- Create: `src/EverythingLabeled.Tray/Copy.cs`（气泡/菜单文案）
- Create: `src/EverythingLabeled.Tray/Assets/tray.ico`（或嵌入简单图标）
- Modify: `src/EverythingLabeled.Tray/EverythingLabeled.Tray.csproj`（ProjectReference Shared；Copy Host 输出 Target，照抄 Command 的 `CopyExplorerHostOutput`）
- Modify: `tests/EverythingLabeled.Tray.Tests/`（纯函数测：`FindLauncher.ResolveHostPath`、`StartupRun` 可测包装若抽出）

**Interfaces:**

- Consumes: `HotkeyParser`, `TrayConfigStore`, `FindHostIpc`
- Produces:

```csharp
namespace EverythingLabeled.Tray;

public static class FindLauncher
{
    public const string HostExeName = "EverythingLabeled.ExplorerHost.exe";
    public const int DebounceMs = 300;

    public static string ResolveHostPath(string trayDirectory);
    // Path.Combine(trayDirectory, HostExeName)

    /// <summary>先 TrySendActivate；失败则 Start Host --mode find。缺 exe → false。</summary>
    public static bool TryActivateOrStart(string trayDirectory, string pipeName, TimeSpan activateTimeout);
}

public static class StartupRun
{
    public const string ValueName = "EverythingLabeled.Tray";
    // HKCU\Software\Microsoft\Windows\CurrentVersion\Run

    public static void Apply(bool enabled, string trayExeAbsolutePath);
    // enabled → SetValue；否则 DeleteValue（缺则忽略）
    public static bool IsEnabled();
}

public sealed class HotkeyRegistration : IDisposable
{
    // 隐藏 Message-only 窗体或用 NativeWindow；RegisterHotKey(id=1)；WM_HOTKEY → callback
    public static bool TryRegister(HotkeySpec spec, Action onHotkey, out HotkeyRegistration? reg, out string? error);
}
```

`Copy` 钉死（简体）：

- 菜单：`打开查找` / `开机自启` / `退出`
- 气泡标题：`EverythingLabeled`
- 热键失败：`无法注册全局热键，请改 findHotkey 后重启托盘。`
- 缺 Host：`无法启动查找（未找到 EverythingLabeled.ExplorerHost.exe）。`
- 自启写失败：`无法更新开机自启。`

行为：

1. `Program.Main`：抢 Tray Mutex（`Local\EverythingLabeled.Tray` / `EL_TRAY_MUTEX_NAME`）；失败 `return`。
2. `Application.Run(new TrayApplicationContext())`。
3. Context：`NotifyIcon` Visible；菜单三项；左键/双击 → 防抖后 `TryActivateOrStart`。
4. Load config → Parse hotkey；`Register` 则 `TryRegister`，失败气泡一次；`Disabled` 跳过。
5. `runAtStartup==true` → `StartupRun.Apply(true, exePath)` 幂等；菜单勾选切换 → `TrySetRunAtStartup` + `StartupRun.Apply`；注册表失败则勾选回滚 + 短提示。
6. 退出：UnregisterHotKey、Dispose icon；**不**杀 Host。

- [ ] **Step 1: 写/补测试**

`ResolveHostPath`；可选：用临时目录无 exe 时 `TryActivateOrStart` → false（不抛）。

- [ ] **Step 2: 跑测**

`dotnet test tests/EverythingLabeled.Tray.Tests`

- [ ] **Step 3: 实现 Tray UI + 构建拷贝 Host**

csproj Copy Target 与 Command 相同模式。`dotnet build src/EverythingLabeled.Tray` 后输出目录须有 `EverythingLabeled.ExplorerHost.exe`。

- [ ] **Step 4: 手工冒烟（实现者本机）**

在 Tray 输出目录运行 Tray → `Alt+L` 开 Find；再按前置；关 Find 再按新开；托盘「退出」后 Find 仍在（若已开）。

- [ ] **Step 5: Commit**

```powershell
git add src/EverythingLabeled.Tray tests/EverythingLabeled.Tray.Tests
git commit -m @"
#N 实现 Tray 托盘热键与唤起 Find

"
```

---

### Task 4: Service 保留未知键 + 文档

**Files:**

- Modify: `src/EverythingLabeled.Service/Hosting/ConfigBootstrap.cs`（`WriteAtomic`：若 `File.Exists(configPath)`，`JsonNode.Parse` → `JsonObject`，写入/覆盖 `baseUrl`/`token`/`confirmUntag`/`previewLimit`/`maxConcurrency` camelCase，保留其余键；再缩进序列化原子写。新建文件行为与现网一致，可不含 `findHotkey`。）
- Create: `tests/EverythingLabeled.Service.Tests/ConfigBootstrapPreserveTests.cs`
- Modify: `docs/explorer.md`（Tray 启动、同目录、热键/自启、单实例要点）
- Modify: `docs/README.md`（工程表含 Tray；构建提示）
- Modify: `plans/2026-10-07-tray-find-hotkey.md`（状态改为实现中/已实现计划就绪）

- [ ] **Step 1: 写失败测试**

临时目录写 JSON：`{"baseUrl":"http://127.0.0.1:5123","token":"<64hex>","findHotkey":"Ctrl+K","extra":true}`，构造合法 `AppConfig` 调 `WriteAtomic`，读回仍含 `findHotkey` 与 `extra`，且 baseUrl/token 为写入值。

- [ ] **Step 2: 跑测失败 → 实现 → 测过**

Run: `dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~ConfigBootstrapPreserve`

- [ ] **Step 3: 更新 docs**

- [ ] **Step 4: 全量相关测试**

```powershell
dotnet test tests/EverythingLabeled.ExplorerHost.Tests
dotnet test tests/EverythingLabeled.Tray.Tests
dotnet test tests/EverythingLabeled.Service.Tests --filter FullyQualifiedName~ConfigBootstrap
```

- [ ] **Step 5: Commit**

```powershell
git add src/EverythingLabeled.Service tests/EverythingLabeled.Service.Tests docs/explorer.md docs/README.md plans/2026-10-07-tray-find-hotkey.md
git commit -m @"
#N Service 写配置保留未知键并更新 Tray 文档

"
```

---

## Self-review (plan)

| Spec 项 | Task |
|---|---|
| WinForms Tray + RegisterHotKey + NotifyIcon | T3 |
| findHotkey / runAtStartup + 保留未知键 | T2、T4 |
| Find Mutex + 管道 Activate\n 全入口 | T1 |
| 同目录 Host；无 EL_HOST_PATH | T3 |
| Tray/Find Mutex 可覆盖 | T1、T3 |
| 300ms 防抖；退出不杀 Host | T3 |
| HKCU Run `EverythingLabeled.Tray` | T3 |
| Service 不强制写出新键、重写保留 | T4 |
| 单测热键/配置/单实例 | T1–T2、T4 |

无 TBD/「类似 Task N」占位；类型名跨 Task 一致（`FindHostIpc`、`HotkeySpec`、`TrayConfigStore`）。
