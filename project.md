# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线与 #8–#14 / Tray / Find UI / 扩展 `#121`–`#122` 已合入 `master` 并推送；公有仓库 https://github.com/lanwah/EverythingLabeled → [骨架](plans/2026-09-27-service-skeleton.md) · [CRUD](plans/2026-09-29-crud-and-query.md) · [CLI](docs/cli.md) · [MCP](docs/mcp.md) · [扩展](docs/browser-extension.md) · [Explorer](docs/explorer.md)
- Explorer / Tray 本机验收通过（2026-10-17）：稀疏包 `0.1.8.0` 右键四菜单青绿图标、Host 四页深色皮肤（打标/去标芯片、别名联想）、Tray `Alt+L` → [docs/explorer.md](docs/explorer.md) · [菜单图标](plans/2026-10-17-explorer-menu-icons.md) · [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- Find：末词 suggest + 实时 preview、芯片 + AND/OR、无边框深色标题栏；结果行文件/文件夹/网页图标已落到 Host（`KindIcons.xaml`）→ [Find UX](plans/confirmed/2026-10-07-find-ux-review.md) · [芯片](plans/confirmed/2026-10-07-find-chip-and-or-review.md) · [图标预览](design/explorer-ui/icons.html) · [docs/explorer.md](docs/explorer.md)
- Find 关窗/失焦改为 Hide 保活（Mutex/管道不释放）；`Alt+L` 再开走管道 Activate。打标/去标/看标签标题栏关闭改为真正 `Close`（此前与 Find 共用 `HideFind`，非 Find 模式直接返回，点 × 无反应）。登录后第一次 Find 仍是冷启动 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md) · [审核 §I](plans/confirmed/2026-10-07-tray-find-hotkey-review.md)
- 浏览器扩展：预览芯片 + AND/OR；打标无精确别名时自动勾选「创建缺失标签」（对齐 Explorer Host）；工具栏图标与 Tray 共用青绿标签（`el.ico` 的 16/32/48，128 由 256 帧缩放）→ [docs/browser-extension.md](docs/browser-extension.md)

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
- Find UI：芯片 + 全局 AND/OR（默认 AND）；Find 无边框深色标题栏；Enter 落芯片；不做负词芯片/行内操作图标 → [芯片审核](plans/confirmed/2026-10-07-find-chip-and-or-review.md)
- Explorer Host 四页皮肤：以 [design/explorer-ui/](design/explorer-ui/index.html) 为视觉基线（打标/去标/看标签对齐 Find 深色；别名联想浮动下拉不撑布局；去标当前标签为带 × 芯片）
- Tray：WinForms 独立进程；默认 `Alt+L`（空/`none` 关热键）；Find 全入口单实例；关窗/失焦 Hide 保活；Run=`EverythingLabeled.Tray`；Host 仅同目录 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- Explorer 菜单图标：Segoe MDL2 字形生成多尺寸 `.ico`，**用 DIB 帧不用 PNG 帧**（PNG 帧在 GDI+ 消费端会解出乱码）；`GetIcon` 返绝对路径且包 try/catch；右键 `find` 用放大镜 `E721`；Host Find 标题栏保持 `E8B7`（与菜单刻意区分）→ [菜单图标存档](plans/2026-10-17-explorer-menu-icons.md) · [docs/explorer.md](docs/explorer.md)

## 待办
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)
- v1 安装器：规格已确认（[配置与安装](plans/2026-09-27-config-and-install.md)），实现计划尚未起草

已完成记录 → [plans/done.md](plans/done.md)

## 风险
- 查询「必须快」依赖缓存与查询引擎；#10 默认 Memory 已合入，Redis 为可选项
- Explorer / 浏览器扩展联调依赖 Service 业务 API，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
起草 v1 per-user 安装器实现计划（Service+CLI self-contained、config 引导、HKCU Run/PATH、装完 health 轮询；Explorer/Tray/扩展仍另发）→ [配置与安装](plans/2026-09-27-config-and-install.md)
