# EverythingLabeled 配置与安装规格

日期：2026-09-27  
状态：已确认（v1 基线）  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [CLI 规格](./2026-09-26-cli.md)
- [浏览器扩展规格](./2026-09-27-browser-extension.md)
- [Explorer 集成规格](./2026-09-27-explorer-integration.md)
- [MCP 规格](./2026-09-27-mcp.md)
- [路线图 backlog #7](./2026-09-23-roadmap-backlog.md)

## 1. 设计摘要

- **形态**：per-user exe 安装器；**当前用户**会话进程 + **HKCU 登录自启**（非 Windows Service / SCM）。
- **组件**：安装包含 **Service + CLI（`el`）**；Explorer / 浏览器扩展 / MCP **另发**。
- **配置 SSOT**：`%AppData%/EverythingLabeled/config.json`（`baseUrl` + `token` 等）；Service 与可读该文件的客户端共用；**浏览器扩展仍手填、不自动同步**。
- **服务端非密钥项**：安装目录内 `appsettings.json`（如 `Cache:Provider`），**不含** token。
- **首次**：探测端口（首选 **5123**，范围 **5123–5132**）+ 静默生成高熵 token，写入 config；之后端口/token **固定**，升级不覆盖。
- **数据**：`%AppData%/EverythingLabeled/data/`；日志建议 `logs/`。
- **卸载**：询问是否删除 AppData；默认建议保留。

## 2. 目录与文件布局

| 路径 | 用途 |
|---|---|
| `%LocalAppData%\EverythingLabeled\` | 安装根：Service / CLI 二进制、随包 `appsettings.json`、卸载元数据 |
| `%LocalAppData%\EverythingLabeled\EverythingLabeled.Service.exe` | 守护进程（逻辑名；实现可微调文件名） |
| `%LocalAppData%\EverythingLabeled\el.exe` | CLI |
| `%AppData%\EverythingLabeled\config.json` | 客户端 + Service 共用配置 |
| `%AppData%\EverythingLabeled\data\` | SQLite 等用户数据（库文件名留给 Service 骨架） |
| `%AppData%\EverythingLabeled\logs\` | Service 滚动日志（建议；勿写入安装目录） |

要点：

- 程序在 LocalAppData；用户状态在 AppData 树下（便于整目录备份；v1 **不**依赖漫游同步）。
- CLI **只读** config（见 [CLI §2](./2026-09-26-cli.md)）；写入仅安装器或 Service 首次引导。
- `appsettings.json` 仅非密钥服务端项。

### 2.1 `config.json`（客户端契约，与架构一致）

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<local-secret>",
  "confirmUntag": true,
  "previewLimit": 16,
  "maxConcurrency": 4
}
```

- `baseUrl` / `token`：必填（安装器或首次引导生成）。
- 可选键缺省与 [架构 §6](./2026-09-22-local-daemon-architecture.md)、[CLI §2](./2026-09-26-cli.md) 一致。
- 未知键：客户端忽略（CLI 已确认）。

## 3. 安装器行为

**形态**：per-user exe，**无需管理员**。

### 3.1 安装 / 升级步骤（顺序）

1. 解压或覆盖到 `%LocalAppData%\EverythingLabeled\`。
2. 若 `%AppData%\EverythingLabeled\config.json` **不存在**：
   - 对 `127.0.0.1` 自 **5123** 起在 **5123–5132** 探测空闲 TCP 端口；
   - 若范围内均不可用 → 安装失败并提示；
   - 生成高熵随机 `token`（≥128 bit；URL-safe 或 hex）；
   - 写入 `config.json`（`baseUrl` = `http://127.0.0.1:<port>`，可选键用缺省）；
   - 创建 `data\`、`logs\`。
3. 若 config **已存在**（升级 / 重装）：**不修改** `baseUrl` / `token` / `data\`。
4. 注册 **HKCU** 登录自启，指向 Service exe。
5. 将安装目录追加到 **用户 PATH**（已存在则跳过）。
6. **立即启动** Service。
7. 完成页：说明 config 路径；提示浏览器扩展须手动复制 `baseUrl` / `token`。

### 3.2 卸载

1. 停止 Service 进程（若在运行）。
2. 移除 HKCU 自启项。
3. 从用户 PATH 移除安装目录。
4. 删除 `%LocalAppData%\EverythingLabeled\` 程序文件。
5. **询问**是否删除 `%AppData%\EverythingLabeled\`（config + data + logs）；**默认建议保留**。

## 4. 运行时

### 4.1 绑定与鉴权

- 仅绑定 `127.0.0.1`。
- 端口与 token：启动时从同一份 `config.json` 读取（解析 `baseUrl` 得端口；失败 → 启动失败）。
- Bearer：与 config 中 `token` 校验（实现细节留给 Service 骨架；建议常量时间比较）。

### 4.2 单实例与端口冲突

- 进程级互斥（如每用户会话命名 Mutex）。
- **已有本实例**：安静 **exit 0**（避免登录自启报错骚扰）。
- **无本实例但端口被其它程序占用**：非 0 退出 + 日志；**不改** config。
- config 写入端口后，运行期 **永不**因冲突自动换端口。

### 4.3 缺 config 兜底

- 未走安装器时（开发态）：Service 首次启动执行与 §3.1 步骤 2 **同等**引导。
- CLI 仍不写 config；缺文件 → 既有 `config_missing` 行为。

## 5. v1 非目标

- Windows Service（SCM）、托盘壳、图形化服务管理
- MSI / 机器级（需管理员）安装
- 强制依赖 Redis
- token 轮换向导；CLI 写 config / 启停服务（见 [CLI §8](./2026-09-26-cli.md)）
- 将 Explorer / 浏览器扩展 / MCP 打进同一安装包
- 多机同步、自动改端口、DPAPI 独立机密文件

## 6. 交叉引用

| 主题 | 文档 |
|---|---|
| 客户端 config 键与语义 | [架构 §6](./2026-09-22-local-daemon-architecture.md)、[CLI §2](./2026-09-26-cli.md) |
| 扩展手填、不同步 | [浏览器扩展](./2026-09-27-browser-extension.md) |
| HTTP Bearer / 回环 | [HTTP API](./2026-09-26-http-api.md)、架构 |
| 实现 | backlog **#8** Service 骨架；**#11** CLI |

## 7. 自检清单（实现时）

- [ ] 无管理员可完成安装；登录后 Service 自启；`el` 在用户 PATH 可用
- [ ] 首装生成 config（5123–5132 + token）；升级不改 token/端口/data
- [ ] 卸载询问是否删 AppData；保留时重装仍可用原 config
- [ ] 第二实例 exit 0；端口他占失败且 config 不变
- [ ] Service 与 CLI 读同一 `config.json`；`appsettings` 无 token
- [ ] 完成页 / 文档指向 `%AppData%\EverythingLabeled\config.json` 供扩展复制
