# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线已齐；#8–#14、Tray Find（#101–#104）、Find UI（#105）已合入 `master`；本机稀疏包注册已修好（`0.1.8.0`/neutral）且**右键四菜单已恢复并带上图标**（2026-10-16/17；两层原因：打过期产物 + HKCU 经典菜单覆盖；图标：命令不给图标 + 包 Logo 是纯黑占位）→ [骨架](plans/2026-09-27-service-skeleton.md) · [CRUD](plans/2026-09-29-crud-and-query.md) · [CLI](docs/cli.md) · [MCP](docs/mcp.md) · [扩展](docs/browser-extension.md) · [Explorer](docs/explorer.md) · [Tray](docs/explorer.md)
- Find UX/UI：末词 suggest + 实时 preview、Enter 填联想或打开、Host 居中、↓ 一次进下一项、失焦关闭；#105 深色无边框标题栏 + 芯片 + AND/OR（默认 AND）+ 自动聚焦 → [审核](plans/confirmed/2026-10-07-find-ux-review.md) · [芯片审核](plans/confirmed/2026-10-07-find-chip-and-or-review.md) · [视觉审核](plans/confirmed/2026-10-07-find-ui-polish-review.md)
- Explorer Host 四页深色皮肤已落到 WPF（`Themes/HostDark.xaml`；打标/去标/看标签对齐 Find；别名联想浮动 Popup；缺失别名自动勾选「创建缺失标签」）→ [design/explorer-ui/](design/explorer-ui/index.html)
- Tray Find 热键：#101–#104 已合入 `master` → [规格](plans/2026-10-07-tray-find-hotkey.md) · [docs/explorer.md](docs/explorer.md)
- 图标：托盘与 exe 用青绿标签形 `el.ico`（16–256）；右键四项各带青绿 `#2EC4B6` 字形图标（`ElTag/ElUntag/ElTags/ElFind.ico`，`GetIcon` 返绝对路径，字形由 [scripts/make-explorer-icons.ps1](scripts/make-explorer-icons.ps1) 生成）→ [菜单图标存档](plans/2026-10-17-explorer-menu-icons.md)
- 稀疏包重新注册：清单提到 `0.1.8.0` / `neutral`，改用 [scripts/register-explorer.ps1](scripts/register-explorer.ps1) 一键注册（#109 回滚后 `bin` 里的旧清单曾被打进包，导致右键菜单不出现）→ [docs/explorer.md](docs/explorer.md)

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
- Explorer Host 四页皮肤：以 [design/explorer-ui/](design/explorer-ui/index.html) 为视觉基线（打标/去标/看标签对齐 Find 深色；别名联想浮动下拉不撑布局）
- Tray：WinForms 独立进程；默认 `Alt+L`（空/`none` 关热键）；Find 全入口单实例；Run=`EverythingLabeled.Tray`；Host 仅同目录 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- Explorer 菜单图标：Segoe MDL2 字形生成多尺寸 `.ico`，**用 DIB 帧不用 PNG 帧**（PNG 帧在 GDI+ 消费端会解出乱码）；`GetIcon` 返绝对路径且包 try/catch；`find` 用放大镜 `E721` → [菜单图标存档](plans/2026-10-17-explorer-menu-icons.md)

## 待办
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)
- Tray Find 热键：本机手工验收（Alt+L / 托盘菜单 / 单实例 / 自启）若尚未做完可补测
- [ ] 右键四项图标**肉眼验收**（自动化截图受新菜单 = XAML island 限制，需人眼确认）
- [ ] Host 标题栏 `find` 字形是否统一为放大镜 `E721`（现为 `E8B7`，与菜单不一致）→ [菜单图标存档](plans/2026-10-17-explorer-menu-icons.md)

已完成记录 → [plans/done.md](plans/done.md)

## 风险
- 查询「必须快」依赖缓存与查询引擎；#10 默认 Memory 已合入，Redis 为可选项
- Explorer / 浏览器扩展联调依赖 Service 业务 API，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
鼠标右键文件/文件夹，确认四项各显示青绿字形图标（打标/去标/看标签/放大镜），并顺带验收 Host 三页深色皮肤（对齐 [design/explorer-ui/](design/explorer-ui/index.html)；联想浮动；缺失别名自动勾选创建缺失）→ [验收清单](docs/explorer.md)

取舍记录：经典右键菜单与 packed 右键项**不可兼得**（当前选 Win11 新式菜单）→ [排查存档与恢复](plans/2026-10-16-explorer-menu-missing.md)
