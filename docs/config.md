# 本机配置（`config.json`）

实现说明：AppData 下 **`config.json` 键表、缺省、谁读谁写**。契约与安装行为以 [配置与安装规格](../plans/2026-09-27-config-and-install.md) 为准；架构摘要见 [本地守护进程架构 §6](../plans/2026-09-22-local-daemon-architecture.md)。

## 文件位置

| 项 | 说明 |
|---|---|
| 默认路径 | `%AppData%\EverythingLabeled\config.json`（Windows 示例：`C:\Users\<你>\AppData\Roaming\EverythingLabeled\config.json`） |
| 覆盖根目录 | 环境变量 **`EL_APPDATA`** → 其下仍为 `config.json`、`data/`、`logs/`（与 Service `AppPaths`、Tray `TrayConfigStore` 一致） |
| 仓库内 | **无**随仓模板；首次由 Service **引导**生成（见 [Service 骨架](./service-skeleton.md)） |
| 同目录其它文件 | `data/el.db`、`logs/`（见 [Service 骨架 §3](./service-skeleton.md)） |

程序目录内的 **`appsettings.json`** 只放 Service 非密钥项（如 `Cache:Provider`），**不含** `token`；与 `config.json` 职责分离 → [配置与安装 §1](../plans/2026-09-27-config-and-install.md)、[Service 骨架](./service-skeleton.md)。

## 示例

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<64-hex-lowercase>",
  "confirmUntag": true,
  "previewLimit": 16,
  "maxConcurrency": 4,
  "findHotkey": "Alt+L",
  "runAtStartup": true,
  "findTagLogic": "and"
}
```

键名一律 **camelCase**。未知键：读取方忽略；**写回方须保留**（Service `WriteAtomic` 合并、Tray 改 `runAtStartup` 时保留其余键）→ [配置与安装 §2.1](../plans/2026-09-27-config-and-install.md)。

## 配置项一览

| 键 | 必填 | 缺省 | 约束 / 含义 | 主要读者 |
|---|---|---|---|---|
| `baseUrl` | 是 | （引导时写入） | 本机 Service HTTP 根地址。Service 校验：`http`、`127.0.0.1`、path 空或 `/`、无 query/fragment。首次引导端口常在 **5123–5132**；手改端口可超出该范围，仍须通过校验。 | Service、CLI、MCP、Explorer Host |
| `token` | 是 | （引导时生成） | Bearer 共享密钥：**64 位小写 hex**（32 字节随机）。禁止写入日志。Service **不热加载**——修改后须重启 Service。 | 同上 |
| `confirmUntag` | 否 | `true` | 去标前是否二次确认。 | CLI、`untag`；Explorer Host 去标（[CLI 规格 §5.2](../plans/2026-09-26-cli.md)） |
| `previewLimit` | 否 | `16` | Find / 预览列表条数；有效 **1–50**，越界按缺省处理（`ElConfigLoader`）。 | Explorer Host |
| `maxConcurrency` | 否 | `4` | 批量打标/去标并发；有效 **1–16**。 | Explorer Host |
| `findHotkey` | 否 | `"Alt+L"` | 托盘全局热键打开 Find；**空串或 `none`** → 不注册。语法见 [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md)。改 JSON 后 **重启 Tray**。 | Tray |
| `runAtStartup` | 否 | `true` | 是否登记 HKCU Run **`EverythingLabeled.Tray`**（与 Service 的 `EverythingLabeled` 并存）。托盘菜单可切换并 **写回** 此键。 | Tray |
| `findTagLogic` | 否 | 视为 `and` | Find 多标签预览：`"and"` 或 `"or"`（拼查询 `q`）；**Find UI 无开关**。改后 **重启 Host / Find**。 | Explorer Host（`ExplorerConfigLoader.ReadFindUseOr`） |

实现锚点（便于对照代码）：

- 共用五项：`src/EverythingLabeled.Client/ElConfig.cs`（`ElConfigLoader`）
- Service 读/写/校验：`src/EverythingLabeled.Service/Hosting/ConfigBootstrap.cs`
- Tray：`src/EverythingLabeled.Tray/TrayConfigStore.cs`
- Find AND/OR：`src/EverythingLabeled.ExplorerHost/ExplorerConfig.cs`

## 各组件如何读配置

| 组件 | 默认读 AppData `config.json` | 覆盖方式 | 写回 |
|---|---|---|---|
| **Service** | 是（`EL_APPDATA` 或默认根） | 无（仅文件） | 缺文件时引导生成；`WriteAtomic` 合并写入时保留未知键 |
| **CLI `el`** | 是 | `--config` / `--base-url` / `--token` > `EL_CONFIG` / `EL_BASE_URL` / `EL_TOKEN` | **不写回** → [CLI](./cli.md) |
| **MCP** | 是 | 同 CLI 优先级 | **不写回** → [MCP](./mcp.md) |
| **Explorer Host** | 是 | **无**（规格：忽略 `EL_*`，不用 flag 覆盖） | **只读** → [Explorer](./explorer.md) |
| **Tray** | 是 | 主要读 `findHotkey` / `runAtStartup`；缺文件用 Tray 内置缺省 | 可写 `runAtStartup` |
| **浏览器扩展** | **不读** AppData | Options / `chrome.storage.local` 手填（键名可对齐，如 `baseUrl`、`token`、`findTagLogic`） | 扩展自有存储，**不与** AppData 自动同步 → [浏览器扩展](./browser-extension.md) |

CLI / MCP 优先级（与规格一致）：**命令行 flag > 环境变量 > 默认 `config.json` 路径**。

## `baseUrl` 与 `token`（细则）

**`baseUrl`**（Service 侧，`ConfigBootstrap.TryValidateBaseUrl`）：

- scheme 仅 `http`
- host 仅 `127.0.0.1`（拒 `localhost`、`[::1]` 等）
- pathname 为空或 `/`
- 无 query、无 fragment

与 [浏览器扩展 Options 校验](../plans/2026-09-27-browser-extension.md) 对齐。CLI `ElConfigLoader` 解析 `baseUrl` 时允许 `https`，但实际 Service 只接受上述形式，客户端应与之一致。

**`token`**：安装/引导生成规则见 [配置与安装 §2.3](../plans/2026-09-27-config-and-install.md)。Service 启动校验格式；业务请求头 `Authorization: Bearer <token>`。

## 与 `appsettings.json` 的分工

| 文件 | 位置 | 内容 |
|---|---|---|
| `config.json` | `%AppData%\EverythingLabeled\` | `baseUrl`、`token` 及上表客户端可选键 |
| `appsettings.json` | Service 安装/输出目录 | 如 `Cache:Provider`、Serilog 等；升级时随包覆盖 |

见 [架构 §6](../plans/2026-09-22-local-daemon-architecture.md)、[查询缓存](./query-cache.md)。

## 修改后何时生效

| 变更 | 生效方式 |
|---|---|
| `baseUrl` / `token` | 重启 **Service**；所有客户端改用新值 |
| `confirmUntag`、`previewLimit`、`maxConcurrency` | 重启 **Explorer Host**（或重新打开对应窗口）；CLI 下次进程即读 |
| `findTagLogic` | 重启 **Host / Find** |
| `findHotkey` | 重启 **Tray** |
| `runAtStartup` | Tray 读配置或菜单写回后，配合 `StartupRun` 更新注册表 |

## 规格与审核引用

| 主题 | 文档 |
|---|---|
| 安装、目录、原子写、静默卸载 | [配置与安装](../plans/2026-09-27-config-and-install.md) · [审核](../plans/confirmed/2026-09-27-config-and-install-review.md) |
| 架构与安全模型 | [本地守护进程架构 §6](../plans/2026-09-22-local-daemon-architecture.md) |
| CLI 配置与 `confirmUntag` | [CLI 规格 §2](../plans/2026-09-26-cli.md) · [CLI 实现说明](./cli.md) |
| Tray 热键与自启 | [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md) · [实现计划](../plans/2026-10-07-tray-find-hotkey-impl.md) |
| Find 多标签 AND/OR 改为配置项 | [芯片 AND/OR 审核](../plans/confirmed/2026-10-07-find-chip-and-or-review.md) |
| Explorer Host 依赖 | [Explorer](./explorer.md) · [Explorer 规格](../plans/2026-09-27-explorer-integration.md) |
| 扩展不读 AppData | [浏览器扩展规格](../plans/2026-09-27-browser-extension.md) · [扩展说明](./browser-extension.md) |
