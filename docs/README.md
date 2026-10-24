# EverythingLabeled 文档

本目录放**实现说明与开发指引**（如何运行、目录约定、已落地行为）。设计规格与契约以 [`plans/`](../plans/) 为准；看板活状态见 [`project.md`](../project.md)。

## 支持的操作系统与环境要求

v1 **以 Windows 为优先与验收平台**（配置默认落在 `%APPDATA%\EverythingLabeled\`）。Service / CLI / MCP / Client / PathUri 使用 `net8.0`、避开 Windows-only API，便于日后多平台；**Explorer 稀疏包、ExplorerHost、Tray 为 Windows 专用**（`net8.0-windows`，开发与注册按 **x64**）。浏览器扩展为 Chrome / Edge MV3，不绑定 OS。

| 用途 | 要求 |
|---|---|
| 编译 / 跑 .NET 工程与测试 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 全量验收（含右键菜单、Tray 热键） | Windows 10/11 x64 |
| 仅 Service / CLI / MCP | 理论上其它 OS 可编可跑；v1 未作正式验收 |
| 构建浏览器扩展（`ext/`） | Node.js ≥ 20（见 [浏览器扩展](./browser-extension.md)） |
| 加载扩展 | Chrome 或 Edge（MV3） |
| 查询缓存 Redis（可选） | 仅当配置 `Cache:Provider=Redis` 时需要可达的 Redis；默认 Memory，无需 |

架构表述见 [本地守护进程架构](../plans/2026-09-22-local-daemon-architecture.md)。

## 组件关系与如何工作

v1 形态：**本机守护进程为唯一数据入口**，CLI / MCP / Explorer / 浏览器扩展都是薄客户端。客户端不直连 SQLite；读写一律经 Service 的 HTTP JSON + Bearer（`127.0.0.1`，前缀 `/v1/...`）。架构基线见 [本地守护进程架构](../plans/2026-09-22-local-daemon-architecture.md)。

### 总览

```text
                    ┌─────────────────────────────────────┐
  el (CLI) ─────────┤                                     │
  MCP stdio ────────┤  HTTP + Bearer                      │
  Explorer Host ────┤  ───────────────────────────────►   │  EverythingLabeled.Service
  浏览器扩展 (ext/) ─┤                                     │    ├─ SQLite (el.db)
                    │                                     │    └─ ITagQueryCache
                    └─────────────────────────────────────┘
```

| 角色 | 位置 | 职责 |
|---|---|---|
| **Service** | `src/EverythingLabeled.Service` | Minimal API、持久化、查询/缓存、本机鉴权；唯一写库方 |
| **PathUri** | `src/EverythingLabeled.PathUri` | 路径 / `file://` / `http(s)` URI 规范化（Service 与客户端共用规则） |
| **Client** | `src/EverythingLabeled.Client` | `ElConfig` / `IElApi` / `HttpElApi`：读配置、发 HTTP、DTO |
| **CLI `el`** | `src/EverythingLabeled.Cli` | 命令行打标/查标；确认与闸门在进程内 |
| **MCP** | `src/EverythingLabeled.Mcp` + `Mcp.Tools` | Agent 工具面（stdio）；工具实现调 Client，不引用 Service |
| **Explorer** | `ExplorerCommand` + `ExplorerHost` + `ExplorerShared` | 右键菜单 DLL 只起 Host；业务与 HTTP 全在 WPF Host |
| **Tray** | `EverythingLabeled.Tray` | 托盘 + 全局热键打开 Find；构建拷 Host 同目录；见 [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md) · [实现计划](../plans/2026-10-07-tray-find-hotkey-impl.md) |
| **浏览器扩展** | `ext/`（MV3） | 当前页 URL 打标；Options 手填 `baseUrl`+token，**不读** AppData |

### 工程依赖（编译期）

```text
EverythingLabeled.PathUri
        ▲
        ├── EverythingLabeled.Service
        └── EverythingLabeled.Client
                    ▲
                    ├── EverythingLabeled.Cli
                    ├── EverythingLabeled.Mcp.Tools ──► EverythingLabeled.Mcp
                    └── EverythingLabeled.ExplorerHost
                              ▲
EverythingLabeled.ExplorerShared ──┤
                              ├── EverythingLabeled.ExplorerCommand
                              │     （编 Host 后把 Host 整目录拷到 Command 输出旁）
                              └── EverythingLabeled.Tray
                                    （编 Host 后把 Host 整目录拷到 Tray 输出旁）

ext/  …… 独立 Node/Vite 工程，无 .NET ProjectReference
```

要点：

- **Service 只依赖 PathUri**，不依赖 Client / CLI / MCP / Explorer / Tray。
- **Client 依赖 PathUri**；CLI、MCP.Tools、ExplorerHost 依赖 Client，避免各端各自拼 HTTP。
- **MCP host 依赖 Client + Mcp.Tools**；禁止引用 `EverythingLabeled.Service` 或 AspNetCore MCP 包。
- **ExplorerCommand / Tray** 依赖 Shared，并引用 Host 以便同目录启动 `EverythingLabeled.ExplorerHost.exe`；Tray **不**调业务 HTTP。
- **浏览器扩展**与 .NET 无编译依赖，运行时只认手工配置的 Service `baseUrl`。

### 运行时如何协作

1. **起 Service**  
   监听回环地址；端口与 token 写入（或读取）`%APPDATA%\EverythingLabeled\config.json`。键表与谁读谁写见 [本机配置](./config.md)。探活 `GET /v1/health`；业务 API 在 `/v1/...`。

2. **可读 AppData 的客户端**（CLI、MCP、Explorer Host）  
   按 [本机配置](./config.md) 与各端文档的优先级解析 `--config` / 环境变量 / AppData，拿到 `baseUrl` + `token`，经 **Client**（或等价 `fetch`）调 Service。配置默认不写回。

3. **浏览器扩展**  
   Options 手填与 AppData **同名键**，但不自动同步；需从 `config.json` 手工复制。仅 service worker 发请求。

4. **Explorer 右键链路**  
   `ExplorerCommand`（稀疏包 COM）把选中路径写入临时 **paths-file** → 即发即忘启动同目录 **ExplorerHost** → Host 读 Shared 约定的文件 → 用 Client 打标/去标/查标/按标签查找。菜单 DLL 不做 HTTP。

5. **Tray 热键打开 Find**  
   常驻托盘注册全局热键 → 唤起/前置同目录 Host `--mode find`（Find 单实例 IPC）；不调业务 HTTP。详见 [Explorer](./explorer.md) / [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md)。

6. **数据流**  
   任意端的写操作都进 Service → SQLite；写成功后 bump `data_version`，查询缓存按版本自然失效。查询建议 / 预览 / 完整结果优先走 `ITagQueryCache`。

未起 Service 时，各客户端只能连失败或（MCP）返回 `connection_failed` / `config_missing`；没有旁路本地库。

## 编译

仓库根对 `EverythingLabeled.sln` 执行 `dotnet build`，并接着构建浏览器扩展。需要 .NET 8 SDK；扩展需要 Node.js ≥ 20（见 [浏览器扩展](./browser-extension.md)）。默认 Debug。

```bat
build.bat
build.bat -Configuration Release
```

```powershell
.\build.ps1
.\build.ps1 -Configuration Release
```

`build.bat` 调用同目录 `build.ps1`：先 `dotnet build`，再调用 `build-ext.ps1`（`ext/` 下 `npm run build`，缺 `node_modules` 时先 `npm ci`）。编译前会结束本仓库输出目录里仍在运行的 `EverythingLabeled.Tray` 与 `EverythingLabeled.ExplorerHost`（Find 关窗只隐藏，托盘退出也不会结束 Host，否则会锁住 DLL）。

只编扩展：`build-ext.bat` / `.\build-ext.ps1`。只编 .NET：`dotnet build EverythingLabeled.sln`（可加 `-c Release`；直接 `dotnet build` 不会先结束 Tray/Host，也不会编扩展）。

## 分项文档

| 文档 | 对应实现 | 说明 |
|---|---|---|
| [本机配置](./config.md) | AppData `config.json` | 键表、缺省、覆盖优先级、与 `appsettings.json` 分工；规格见 [配置与安装](../plans/2026-09-27-config-and-install.md) |
| [Service 骨架](./service-skeleton.md) | backlog **#8** | 守护进程工程结构、启动、配置、health/鉴权、测试 |
| [CRUD 与查询](./crud-and-query.md) | backlog **#9** | 标签/资源/关系/查询落地布局、写路径 bump、缓存桩、如何跑测试 |
| [查询缓存](./query-cache.md) | backlog **#10** | Memory/Redis/Null、`Cache:*`、opt-in Redis 测 |
| [CLI](./cli.md) | backlog **#11** | `el` 用法与命令手册、配置优先级、工程布局与测试；契约见 [CLI 规格](../plans/2026-09-26-cli.md) |
| [MCP](./mcp.md) | backlog **#12** | stdio host、`--help`/`--version`、配置优先级、Cursor `mcp.json`、Tools 测试 |
| [浏览器扩展](./browser-extension.md) | backlog **#13** | `ext/` 构建与加载、Options 手工配置、Service 依赖、手工联调清单 |
| [Explorer 上下文菜单](./explorer.md) | backlog **#14** | 稀疏包注册/卸载、菜单四项图标、Host 四 mode 自测、AppData 依赖、手工验收清单；含 Tray Find 热键说明 |
