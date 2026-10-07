# Tray：全局热键打开 Find

日期：2026-10-07  
状态：设计已确认（待写实现计划）  
审核记录：[Tray Find 热键审核](./confirmed/2026-10-07-tray-find-hotkey-review.md)  
关联：

- [Explorer 集成规格](./2026-09-27-explorer-integration.md)
- [roadmap #16](./2026-09-23-roadmap-backlog.md)（本切片从「托盘壳」中先行抽出）

## 1. 目标

Windows 全局快捷键（默认 `Alt+L`）快速打开「按标签查找」；可选开机自启；配置可改热键，本阶段无设置 UI。

## 2. 非目标（本切片）

- 设置窗 / 热键录制 UI
- 多热键、多动作绑定
- 文件跟标、全盘索引、关系 CRUD
- Tray 内嵌 suggest/preview 或其它业务 HTTP
- 全局强制单实例 Host（`tag`/`untag`/`tags` 仍可多开）
- 安装器、跨用户会话 IPC

## 3. 架构

```text
[开机 / 用户启动]
    └─ EverythingLabeled.Tray.exe（单实例）
           ├─ NotifyIcon + 菜单
           ├─ RegisterHotKey(findHotkey)
           ├─ 读写 config.json（findHotkey / runAtStartup）
           └─ 热键 / 菜单 / 双击
                  ├─ 管道 Activate → 已有 Find 前置
                  └─ 否则 Start ExplorerHost.exe --mode find

[Explorer 右键「按标签查找」 / CLI]
    └─ ExplorerHost --mode find
           ├─ 抢 Find Mutex → 开窗 + 管道服务端
           └─ 未抢到 → Activate 已有窗后退出
```

| 组件 | 职责 |
|---|---|
| **`EverythingLabeled.Tray`** | 托盘、热键、自启、唤起/激活 Find；不承载业务 UI |
| **`ExplorerHost`（find）** | Find UI；Find 单实例 Mutex + 命名管道服务端 |
| **`config.json`** | 共用 AppData；增 `findHotkey`、`runAtStartup`；各端写回须保留字段 |
| **Service / CLI / Host 业务路径** | 不因 Tray 改变；Host 仍只读业务配置 |

## 4. 配置

路径：`%AppData%\EverythingLabeled\config.json`（与现网一致）。

| 键 | 类型 | 默认 | 含义 |
|---|---|---|---|
| `findHotkey` | string | `"Alt+L"` | 全局热键 |
| `runAtStartup` | bool | `true` | 开机是否启动 Tray |

热键语法：`Ctrl` / `Alt` / `Shift` / `Win` 用 `+` 连接，末段单键（字母、数字、`F1`–`F12`、`Space` 等实现计划钉死集合）。例：`Alt+L`、`Ctrl+Shift+Space`。

读写：

- Tray：读两字段；切换「开机自启」时原子写回 `runAtStartup`（保留其余键）。
- 改 `findHotkey`：本阶段手改 JSON，**重启 Tray** 生效。
- Service `WriteAtomic` / `ElConfigLoader` 及任何写回路径：**不得抹掉**这两字段（及未知键优先保留）。
- Host：可不解析热键字段；业务仍只读。

Tray **不依赖** `baseUrl`/`token` 即可挂托盘；打开 Find 后由 Host 自行处理配置/探活 banner。

## 5. 托盘与生命周期

- Tray **单实例**（命名 Mutex，如 `Local\EverythingLabeled.Tray`）；第二实例退出。
- 图标常驻；左键/双击 = 打开/前置 Find（与热键同路径）。
- 菜单最少：
  1. **打开查找**
  2. **开机自启**（勾选；写 `runAtStartup` + 更新自启登记）
  3. **退出**（注销热键、结束 Tray；**不**杀已开 Host）
- 自启实现：**HKCU Run**（推荐，实现计划钉死；不用 Startup 文件夹双轨）。
- 本切片 = #16「托盘壳」的 **Find 热键子集**；跟标/索引等仍延后。

## 6. Find 单实例与 IPC

仅 `HostMode.Find`：

1. 启动时抢 Mutex（如 `Local\EverythingLabeled.FindHost`）。
2. **抢到**：显示窗口；开命名管道（如 `EverythingLabeled.FindHost`）；收到 `Activate` → 前置主窗；退出时释放。
3. **未抢到**：向管道发 `Activate` 后 **本进程退出**（含右键第二次「按标签查找」）。
4. Tray：先 `Activate`；失败再 `Process.Start` 同目录（或与 Command 一致的解析规则）`ExplorerHost.exe --mode find`。

管道仅本用户 Local；不做提权跨完整性级别。僵尸 Mutex：沿用 AbandonedMutex；仍失败则提示（实现计划写清超时）。

## 7. 错误处理

| 情况 | 行为 |
|---|---|
| 热键注册失败 | 气泡提示一次；Tray 继续；热键失效至改配置并重启 Tray |
| `findHotkey` 解析失败 | 回退 `Alt+L`；回退仍失败则同上 |
| Host.exe 找不到 | 气泡「无法启动查找」；Tray 不崩 |
| config 缺失 | 热键/自启用默认；Tray 可跑 |
| 自启写注册表失败 | 勾选回滚 + 短提示 |

## 8. 测试与验收

- 单测：热键字符串解析；配置读默认；写回保留未知/新字段。
- Find 单实例：第二次启动走 Activate 退出（可测边界用替身）。
- 手工：`Alt+L` 打开/前置；改 JSON 换键；托盘自启；占用键提示；退出 Tray 不杀 Host。

## 9. 规格联动（实现前/中同步）

- Explorer「不做」：去掉笼统「不做托盘」；改为不做完整 #16（跟标等）；注明 Find 单实例例外。
- `docs/explorer.md` / `docs/README.md`：补充 Tray 启动与热键说明。
- roadmap #16：标注「Find 热键 Tray 切片」进行中或完成，其余仍延后。
