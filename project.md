# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线已齐；#8–#14 已合入 `master`；本机稀疏包已 Register 且右键四菜单验收通过 → [骨架](plans/2026-09-27-service-skeleton.md) · [CRUD](plans/2026-09-29-crud-and-query.md) · [CLI](docs/cli.md) · [MCP](docs/mcp.md) · [扩展](docs/browser-extension.md) · [Explorer](docs/explorer.md)
- Find UX：末词 suggest（含简拼）+ 防抖实时 preview；Enter 填联想或打开；无「预览」按钮；Host 屏幕居中 → [审核](plans/confirmed/2026-10-07-find-ux-review.md)
- Tray Find 热键：已在 `feature/tray-find-hotkey` 实现（#101–#104：Find 单实例 IPC、Tray 热键/托盘/自启、Service 写配置保留未知键）→ [规格](plans/2026-10-07-tray-find-hotkey.md) · [实现计划](plans/2026-10-07-tray-find-hotkey-impl.md) · [审核](plans/confirmed/2026-10-07-tray-find-hotkey-review.md)
- Service `GET /` 现返回 HTML 状态页；仓库根 `build.ps1` / `build.bat` 编译 sln → [docs/README.md](docs/README.md)

## 关键决策
- 单体本地守护进程 + SQLite；客户端只走 HTTP（`127.0.0.1` + Bearer）→ [架构](plans/2026-09-22-local-daemon-architecture.md)
- 技术栈 .NET 8（C#）；Windows 优先，Service/CLI 避开 Windows-only API
- 标签：扁平主名 + 可选命名空间；`TagRelation` 网状（`child`/`related`）；查询默认 AND，支持 OR / `-tag` → [数据模型](plans/2026-09-23-data-model.md)
- 文件身份仅路径；改名/移动不自动跟标
- 查询缓存可插拔：默认 Memory，可配 Redis/Null；无缓存基类，只换 `ITagQueryCache`；Redis 用 StackExchange.Redis（非 IDistributedCache）→ [查询缓存](plans/2026-10-06-query-cache.md) · [实现计划](plans/2026-10-06-query-cache-impl.md) · [架构 §5](plans/2026-09-22-local-daemon-architecture.md)
- MCP v1 仅 stdio；HTTP MCP 延后；#12：官方 SDK 2.2.0；闸门进 Client；health 用 `LoadBaseUrl`；缺配置仍进会话 → [MCP](plans/2026-09-27-mcp.md) · [实现计划](plans/2026-10-06-mcp-impl.md)
- Service 骨架：单项目文件夹分层、EF Core Migrations、`el.db`、Serilog、health+鉴权最小集 → [Service 骨架](plans/2026-09-27-service-skeleton.md)
- #9：原子 bump + 写后异步 InvalidatePrefix；查询三分流水线；可插拔缓存（默认 Memory）；Set 前复核；写回执扁平 dataVersion；suggest 无 offset；Cache:* 键名已钉；kind 与 URI 须兼容 → [CRUD §11](plans/2026-09-29-crud-and-query.md) · [查询缓存](plans/2026-10-06-query-cache.md)
- #11：`PathUri` + `Client`（`IElApi`/`HttpElApi`/`ElJson`）+ `el`（SCL 2.0.12）；`--json` 对齐 API 字段；确认/闸门在 CLI；URI 规则与 Service 共用 PathUri → [CLI 实现计划](plans/2026-10-06-cli-impl.md)
- #13：Chrome/Edge 一份 MV3；`ext/`；仅 SW `fetch`；配置手填；客户端只去 `#fragment` → [扩展规格](plans/2026-09-27-browser-extension.md) · [实现计划](plans/2026-10-06-browser-extension-impl.md)
- #14：稀疏包 `IExplorerCommand` + WPF Host；先 Host 后菜单；同目录启 Host；开发期稀疏包注册；不做安装器 → [Explorer 规格](plans/2026-09-27-explorer-integration.md) · [实现计划](plans/2026-10-07-explorer-impl.md)
- Find 简拼只走 Host 末词 suggest，query 引擎仍不认 `NameInitials`；实时 preview、无「预览」按钮、窗口居中 → [Find UX 审核](plans/confirmed/2026-10-07-find-ux-review.md)
- Tray：WinForms 独立进程；默认 `Alt+L`（空/`none` 关热键）；Find 全入口单实例；Run=`EverythingLabeled.Tray`；Host 仅同目录 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)

## 待办
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)
- Tray Find 热键：本机手工验收后合入 `master`（分支 `feature/tray-find-hotkey`）

已完成记录 → [plans/done.md](plans/done.md)

## 风险
- 查询「必须快」依赖缓存与查询引擎；#10 默认 Memory 已合入，Redis 为可选项
- Explorer / 浏览器扩展联调依赖 Service 业务 API，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
本机手工验收 Tray Find 热键（Alt+L / 托盘菜单 / 单实例 / 自启）后合入 master → [docs/explorer.md](docs/explorer.md) · [实现计划](plans/2026-10-07-tray-find-hotkey-impl.md)
