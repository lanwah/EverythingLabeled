# EverythingLabeled 配置与安装规格

日期：2026-09-27  
状态：已确认（v1 基线；含 2026-09-27 grill 修订）  
审核记录：[配置与安装规格审核确认](./confirmed/2026-09-27-config-and-install-review.md)  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [CLI 规格](./2026-09-26-cli.md)
- [浏览器扩展规格](./2026-09-27-browser-extension.md)
- [Explorer 集成规格](./2026-09-27-explorer-integration.md)
- [MCP 规格](./2026-09-27-mcp.md)
- [路线图 backlog #7](./2026-09-23-roadmap-backlog.md)

## 1. 设计摘要

- **形态**：per-user 安装器（行为契约；**技术选型不钉死**）+ **当前用户**会话进程 + **HKCU 登录自启**（非 Windows Service / SCM）。
- **架构**：v1 仅 **x64**；Service / CLI 以 **self-contained** 发布（用户免装 .NET）。
- **组件**：安装包含 **Service + CLI（`el`）**；Explorer / 浏览器扩展 / MCP **另发**。
- **配置 SSOT**：明文 `%AppData%/EverythingLabeled/config.json`（`baseUrl` + `token` 等）；Service 与可读该文件的客户端共用；创建时尽量限制为**当前用户**可读写；**不做 DPAPI**；浏览器扩展仍手填、不自动同步。
- **服务端非密钥项**：安装目录内 `appsettings.json`（如 `Cache:Provider`），**不含** token；升级时**始终用新包覆盖**。
- **首次**：探测端口（首选 **5123**，范围 **5123–5132**）+ 静默生成 token（32 字节 → **64 位小写 hex**），**原子写入** config；之后端口/token **固定**，升级不覆盖；手改端口**可超出**该范围。
- **数据**：`%AppData%/EverythingLabeled/data/`；日志目录 `logs/`（滚动策略留给 Service 骨架）。
- **卸载**：GUI 询问是否删除 AppData（默认建议保留）；静默见 §3.3。

## 2. 目录与文件布局

| 路径 | 用途 |
|---|---|
| `%LocalAppData%\EverythingLabeled\` | 安装根：Service / CLI 二进制、随包 `appsettings.json`、卸载入口与元数据 |
| `%LocalAppData%\EverythingLabeled\EverythingLabeled.Service.exe` | 守护进程（逻辑名；实现可微调文件名） |
| `%LocalAppData%\EverythingLabeled\el.exe` | CLI |
| `%AppData%\EverythingLabeled\config.json` | 客户端 + Service 共用配置 |
| `%AppData%\EverythingLabeled\data\` | SQLite 等用户数据（库文件名留给 Service 骨架） |
| `%AppData%\EverythingLabeled\logs\` | Service 文件日志目录（勿写入安装目录） |

要点：

- 程序在 LocalAppData；用户状态在 AppData 树下（便于整目录备份；v1 **不**依赖漫游同步）。
- **不依赖进程 cwd**：config / data / logs / 安装目录均用绝对路径解析。
- CLI **只读** config（见 [CLI §2](./2026-09-26-cli.md)）；写入仅安装器或 Service 首次引导。
- `appsettings.json` 仅非密钥服务端项。

### 2.1 `config.json`

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<64-hex>",
  "confirmUntag": true,
  "previewLimit": 16,
  "maxConcurrency": 4,
  "findHotkey": "Alt+L",
  "runAtStartup": true,
  "findTagLogic": "and"
}
```

- `baseUrl` / `token`：必填（安装器或首次引导生成）。
- 可选键缺省与 [架构 §6](./2026-09-22-local-daemon-architecture.md)、[CLI §2](./2026-09-26-cli.md) 一致。
- Tray 相关可选键（见 [Tray Find 热键](./2026-10-07-tray-find-hotkey.md)）：`findHotkey`（缺省 `Alt+L`；空串/`none` 表示不注册热键）、`runAtStartup`（缺省 `true`）。Service **不必**在引导时主动写出这两键；**任何重写 config 须保留**已有值及未知键。
- Explorer Find 多标签匹配：`findTagLogic` — `"and"`（缺省，空格 AND）或 `"or"`（`OR` 关键字）；Host 只读，**不在 Find UI 展示**；改后需重启 Host 窗口生效。
- 未知键：客户端忽略（CLI 已确认）；写回方须保留。
- **原子写入**：写临时文件再 replace，避免半截 JSON。

### 2.2 `baseUrl` 校验（与[浏览器扩展](./2026-09-27-browser-extension.md)对齐）

安装器写入、Service 启动读取时均适用：

- scheme 仅 `http`（禁 `https`）
- host 仅 `127.0.0.1`（拒 `localhost` / `[::1]` / 其它）
- pathname 为空或 `/`；拒 query / hash
- 端口可省略（按 URI 默认，http→80）；**首次引导与安装器生成时始终写入显式端口**
- 手改后的端口：只要通过上述校验即可（**不**强制落在 5123–5132）

### 2.3 `token`

- 生成：密码学安全随机 **32 字节** → **64 位小写 hex**
- **禁止**写入日志或完成页默认展示明文；完成页只说明 config **路径**与扩展复制方式

## 3. 安装器行为

**形态**：per-user，**无需管理员**；行为见本节，具体安装框架留给实现。

**建议签名**：发布物建议代码签名；未签名时文档提示可能被 SmartScreen 拦截（**不强制**签名才算发布）。

### 3.1 安装 / 升级步骤（顺序）

1. 若 Service 在跑：先**优雅停止**，超时再强杀；再覆盖 `%LocalAppData%\EverythingLabeled\`。
2. 若 `%AppData%\EverythingLabeled\config.json` **不存在**：
   - 对 `127.0.0.1` 自 **5123** 起在 **5123–5132** 探测空闲 TCP；
   - 若范围内均不可用 → 安装失败并提示；
   - 生成 token（§2.3）；原子写入 `config.json`（显式端口的 `baseUrl` + 可选键缺省）；
   - 创建 `data\`、`logs\`；尽量收紧为当前用户 ACL。
3. 若 config **已存在**：
   - **完整且合法** → **不修改** `baseUrl` / `token` / `data\`；
   - **缺失必填、JSON 损坏或 `baseUrl` 校验失败** → **安装/启动失败**，提示手修；**不**自动备份覆盖或补键（开发态「完全无文件」仍走 §4.3）。
4. 注册 **HKCU\...\Run** 值名 **`EverythingLabeled`** → Service 绝对路径（升级幂等覆盖同名值）。Tray 另用 Run 值名 **`EverythingLabeled.Tray`**（见 [Tray 规格](./2026-10-07-tray-find-hotkey.md)）；**本安装器切片不强制**安装/登记 Tray，但卸载清单须能清除该值（若存在）。
5. 将安装目录追加到 **用户 PATH**（已存在则跳过）；广播环境变更（如 `WM_SETTINGCHANGE`）。已开终端不保证立刻可见，完成页可提一句。
6. 注册 **HKCU Uninstall**（「应用和功能」）：DisplayName = **`EverythingLabeled`**；UninstallString 指向卸载入口。
7. **立即启动** Service；轮询 `GET /v1/health`（短超时，实现建议 10–15s）。失败则安装结果为失败或明确警告（不可假装成功）。
8. 完成页：config 路径；扩展须手动复制 `baseUrl`/`token`；**不**默认展示 token 明文。

覆盖安装目录时 **始终覆盖** 包内 `appsettings.json`。

### 3.2 卸载（交互）

1. 优雅停 Service，超时强杀。
2. 移除 Run 值 `EverythingLabeled`、以及若存在的 `EverythingLabeled.Tray`；移除用户 PATH 中的安装目录、ARP 项。
3. 删除 `%LocalAppData%\EverythingLabeled\`。
4. **询问**是否删除 `%AppData%\EverythingLabeled\`；**默认建议保留**。
5. 若进程无法结束导致文件锁住 → **卸载失败**，保留 ARP，提示结束后重试（不做 RunOnce 延迟删）。

### 3.3 静默开关

| 场景 | 开关 | 行为 |
|---|---|---|
| 静默安装 | `/S` | 无 UI；config 已存在则保留；缺则按 §3.1 生成；装完启动并 health |
| 静默卸载（保留数据） | `/S` | 停服务、清自启/PATH/ARP/程序目录；**不**删 AppData |
| 静默卸载并删数据 | `/S /purge` | 同上并删除 `%AppData%\EverythingLabeled\` |

### 3.4 安装失败回滚

尽力清除本次写入的自启、PATH、ARP；**保留**已生成的 config/data；不保证半截 LocalAppData 目录完全消失。

## 4. 运行时

### 4.1 绑定与鉴权

- 仅绑定 `127.0.0.1`；端口来自通过 §2.2 校验的 `baseUrl`。
- 启动时读取同一份 `config.json` 的 `token`；**不热加载**——手改后须重启 Service。
- Bearer 校验细节留给 Service 骨架（建议常量时间比较）。

### 4.2 单实例与端口冲突

- 每用户会话进程互斥（命名 Mutex）。
- **已有本实例**：安静 **exit 0**。
- **无本实例但端口被占**：非 0 退出 + 日志；**不改** config。
- 探测成功到启动之间的端口被抢：按本条失败即可（不重探写回）。

### 4.3 缺 config 兜底

- Service 在 **完全不存在** config 时执行与 §3.1 步骤 2 **同等**引导。
- 引导全过程由 Mutex 保护：并发第二实例等锁后若已有合法 config，则走正常启动/单实例逻辑。
- 文件存在但无效 → 失败，不自动重写（同 §3.1）。
- CLI 仍不写 config；缺文件 → `config_missing`。

## 5. v1 非目标

- Windows Service（SCM）、托盘壳、图形化服务管理
- MSI / 机器级（需管理员）安装；ARM64 双包
- 强制依赖 Redis；强制代码签名
- DPAPI / 独立机密文件；config 热加载；自动改端口
- token 轮换向导；CLI 写 config / 启停服务（见 [CLI §8](./2026-09-26-cli.md)）
- 将 Explorer / 浏览器扩展 / MCP 打进同一安装包
- 多机同步；安装器具体技术框架选型

## 6. 交叉引用

| 主题 | 文档 |
|---|---|
| 客户端 config 键与语义 | [架构 §6](./2026-09-22-local-daemon-architecture.md)、[CLI §2](./2026-09-26-cli.md) |
| `baseUrl` 校验 | [浏览器扩展 §3.3](./2026-09-27-browser-extension.md) |
| HTTP Bearer / 回环 / health | [HTTP API](./2026-09-26-http-api.md)、架构 |
| 实现 | backlog **#8** Service 骨架；**#11** CLI |

## 7. 自检清单（实现时）

- [ ] x64 self-contained；无管理员可装；Run 值名 `EverythingLabeled`（Service）；若存在则清 `EverythingLabeled.Tray`；ARP DisplayName 同名
- [ ] 用户 PATH + 环境广播；`el` 在新终端可用
- [ ] 首装：5123–5132 + 64 hex token、原子写、用户 ACL；升级不改 token/端口/data；覆盖 appsettings
- [ ] 无效 config → 失败不覆盖；无文件 → Service 可 Mutex 引导
- [ ] 升级/卸载：优雅停→超时强杀；装完 health 探活
- [ ] `/S`、`/S /purge`；交互卸载询问 AppData；锁文件失败不留 RunOnce
- [ ] `baseUrl` 与扩展同规则；仅绑 `127.0.0.1`；不热加载；日志无 token
- [ ] 第二实例 exit 0；端口他占失败且 config 不变
- [ ] 完成页只给路径，不默认展示 token 明文
