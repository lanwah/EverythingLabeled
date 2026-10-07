# Tray：全局热键打开 Find

日期：2026-10-07  
状态：已实现（`feature/tray-find-hotkey`，#101–#104）→ [实现计划](./2026-10-07-tray-find-hotkey-impl.md)  
审核记录：[Tray Find 热键审核](./confirmed/2026-10-07-tray-find-hotkey-review.md)  
关联：

- [Explorer 集成规格](./2026-09-27-explorer-integration.md)
- [配置与安装](./2026-09-27-config-and-install.md)（Run 值名并存）
- [roadmap #16](./2026-09-23-roadmap-backlog.md)（本切片从「托盘壳」中先行抽出）

## 1. 目标

Windows 全局快捷键（默认 `Alt+L`）快速打开「按标签查找」；可选开机自启；配置可改热键，本阶段无设置 UI。

## 2. 非目标（本切片）

- 设置窗 / 热键录制 UI
- 多热键、多动作绑定
- 文件跟标、全盘索引、关系 CRUD
- Tray 内嵌 suggest/preview 或其它业务 HTTP
- 全局强制单实例 Host（`tag`/`untag`/`tags` 仍可多开）
- 安装器把 Tray 打进主包（仍另发；首次靠手动或脚本启 Tray）
- 跨用户会话 / 跨完整性级别 IPC
- Tray 文件日志

## 3. 架构

```text
[开机 HKCU Run EverythingLabeled.Tray / 用户手动]
    └─ EverythingLabeled.Tray.exe（WinForms；单实例）
           ├─ NotifyIcon + 菜单
           ├─ RegisterHotKey(findHotkey)（可关闭）
           ├─ 读写 config.json（findHotkey / runAtStartup）
           └─ 热键 / 菜单 / 双击（≈300ms 防抖）
                  ├─ 管道 Activate → 已有 Find：Restore + 尽力前置
                  └─ 否则 Start <TrayDir>\ExplorerHost.exe --mode find

[Explorer 右键「按标签查找」 / CLI / 其它]
    └─ ExplorerHost --mode find
           ├─ 抢 Find Mutex → 开窗 + 管道服务端
           └─ 未抢到 → Activate 已有窗后退出
```

| 组件 | 职责 |
|---|---|
| **`EverythingLabeled.Tray`**（WinForms） | 托盘、热键、自启、唤起/激活 Find；不承载业务 UI；无文件日志 |
| **`ExplorerHost`（find）** | Find UI；Find 单实例 Mutex + 命名管道；关窗释放 |
| **`config.json`** | 共用 AppData；增 `findHotkey`、`runAtStartup`；写回须保留字段 |
| **Service / CLI / Host 业务路径** | Service **不强制**写出新键默认值；**重写时必须保留**已有键 |

**Host 定位**：仅认 **Tray.exe 同目录** 的 `EverythingLabeled.ExplorerHost.exe`（与 Command「同目录」约定一致）。开发用脚本 publish/复制到同一输出目录；产品路径解析 **无** `EL_HOST_PATH` 例外。

## 4. 配置

路径：`%AppData%\EverythingLabeled\config.json`。

| 键 | 类型 | 默认 | 含义 |
|---|---|---|---|
| `findHotkey` | string | `"Alt+L"` | 全局热键；见 §4.1 |
| `runAtStartup` | bool | `true` | 开机是否启动 Tray |

### 4.1 热键语法

- 修饰：`Ctrl` / `Alt` / `Shift` / `Win`，`+` 连接；大小写不敏感；段间允许空白（如 `Alt + L`）。
- 主键仅：`A–Z`、`0–9`、`F1`–`F12`、`Space`（不支持方向键 / Oem / 小键盘别名）。
- **空串**或（规范化后）`none` → **不注册**热键；托盘菜单仍可打开 Find。
- 其它非法值 → 回退 `Alt+L`；回退注册仍失败则按 §7 提示。
- 改 `findHotkey`：手改 JSON，**重启 Tray** 生效。

### 4.2 读写

- Tray：读两字段；切换「开机自启」时读-改-写 `runAtStartup`（保留其余/未知键；失败短重试 1–2 次）。
- Service / 其它写回：**不得抹掉**这两字段（及未知键优先保留）；**不必**在 Bootstrap 主动写入默认值。
- Host：可不解析热键字段。
- Tray **不依赖** `baseUrl`/`token` 即可挂托盘。

## 5. 托盘与生命周期

- Tray **单实例**：Mutex 默认 `Local\EverythingLabeled.Tray`；第二实例 **静默退出**。测试可用环境变量覆盖（如 `EL_TRAY_MUTEX_NAME`）。
- 图标常驻；左键/双击 = 打开/前置 Find（与热键同路径）。
- 菜单最少：**打开查找**、**开机自启**（勾选）、**退出**（注销热键；**不**杀已开 Host）。
- 自启：**HKCU Run** 值名 **`EverythingLabeled.Tray`** → Tray 绝对路径（与 Service 的 Run 值 `EverythingLabeled` **并存**；日后卸载须两项都清）。启动时若 `runAtStartup==true` 则幂等写入；勾选关闭则删除该 Run 值。
- **首次**：无安装器自动拉 Tray；文档/脚本要求用户手动启动一次；之后靠 Run。
- 本切片 = #16 的 Find 热键子集。

## 6. Find 单实例与 IPC

**所有** Find 入口统一单实例（热键 / 托盘 / 右键 / CLI）。

1. `--mode find` 抢 Mutex（默认 `Local\EverythingLabeled.FindHost`；测试可覆盖如 `EL_FIND_MUTEX_NAME`）。
2. **抢到**：显示窗口；命名管道（默认名与规格钉死，如 `EverythingLabeled.FindHost`）；收到单行 `Activate\n` → 若最小化则 Restore，再**尽力**前置（失败则任务栏闪烁，不视为 P0）。
3. **未抢到**：发 `Activate` 后本进程退出。
4. Tray：先 Activate；失败再 `Process.Start` 同目录 Host。热键/菜单连按约 **300ms** 防抖。
5. **关 Find 窗** = 释放 Mutex/管道；下次热键/菜单 **新开** Host。

管道仅本用户 Local；不做提权跨完整性级别。

## 7. 错误处理

| 情况 | 行为 |
|---|---|
| 热键注册失败（占用等） | 气泡提示一次；Tray 继续；热键失效至改配置并重启 Tray |
| `findHotkey` 非法（非空且非 none） | 回退 `Alt+L`；回退仍失败则同上 |
| Host.exe 找不到 | 气泡「无法启动查找」；Tray 不崩 |
| config 缺失 | 热键/自启用默认；Tray 可跑 |
| 自启写注册表失败 | 勾选回滚 + 短提示 |
| 前置窗口失败 | 尽力而为；任务栏闪烁可接受 |

## 8. 测试与验收

- 单测：热键解析（合法/非法/空/`none`/空白/大小写）；配置读默认；写回保留未知键。
- Find 单实例：第二次启动 Activate 后退出（可覆盖 Mutex 名）。
- 手工：`Alt+L` 打开/前置/最小化恢复；`none` 关热键；改 JSON 换键；托盘自启与 Run 值名；占用键提示；退出 Tray 不杀 Host；同目录缺 Host 提示。

## 9. 规格联动

- Explorer「不做」：已改为不做完整 #16；Find 单实例除外 → [Explorer 规格](./2026-09-27-explorer-integration.md)
- [配置与安装](./2026-09-27-config-and-install.md)：Run 值 `EverythingLabeled.Tray` 与 Service 并存；config 可选键说明
- `docs/explorer.md` / `docs/README.md`：Tray 启动与同目录约定
- roadmap #16：Find 热键切片已抽规格
