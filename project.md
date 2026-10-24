# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线与 #8–#14 / Tray / Find UI / 扩展 `#121`–`#122` 已合入 `master` 并推送；公有仓库 https://github.com/lanwah/EverythingLabeled；[docs/cli.md](docs/cli.md) / [docs/mcp.md](docs/mcp.md) 已含用法与七工具/子命令手册（契约 → [CLI 规格](plans/2026-09-26-cli.md) · [MCP 规格](plans/2026-09-27-mcp.md)）→ [骨架](plans/2026-09-27-service-skeleton.md) · [CRUD](plans/2026-09-29-crud-and-query.md) · [扩展](docs/browser-extension.md) · [Explorer](docs/explorer.md)
- Explorer / Tray：稀疏包 `0.1.10.0`、Host 深色四页 + `host.ico`、Tray `Alt+L` / 重新启动冷启 Find；打标/去标/看标签/管理标签失焦关窗（Find Hide）；打标 Enter 提交、当前标签井区对齐稿；去标隐藏别名与底部按钮、点叉号去除；管理标签 / 管理资源已落地 → [docs/explorer.md](docs/explorer.md) · [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- Find / Host 配置：芯片 + `findTagLogic`；`previewLimit`（标签查询预览，缺省 16）与 `suggestLimit`（拼首联想，缺省 8，1–50）写入 AppData `config.json`，与扩展同键对齐；打标/Find/管理资源 suggest 读配置 → [docs/config.md](docs/config.md) · [Find UX](plans/confirmed/2026-10-07-find-ux-review.md)
- 浏览器扩展 0.1.3：Options「标签查询预览条数」/`previewLimit`、「拼首联想预览条数」/`suggestLimit`（缺省 8，1–16）；Popup 打标/查找双模式；预览资源类型默认仅链接；`kind` 过滤走 API → [docs/browser-extension.md](docs/browser-extension.md) · [HTTP API §3.6](plans/2026-09-26-http-api.md)
- `GET /v1/query` / `preview` 可选 `kind`；右键缺项时查 comhost 是否过期（`CoCreate` `0x80040111`）→ [存档](plans/2026-10-20-explorer-comhost-stale.md)

## 关键决策
- 开源许可证：MIT（根目录 [LICENSE](LICENSE)）
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
- Find UI：芯片输入；多标签逻辑读 `findTagLogic`（默认 `and`），不在 UI 切换；Find 无边框深色标题栏；Enter 落芯片 → [芯片审核](plans/confirmed/2026-10-07-find-chip-and-or-review.md)（UI 切换已改为配置项）
- Explorer Host 四页皮肤：以 [design/explorer-ui/](design/explorer-ui/index.html) 为视觉基线（打标/去标/看标签对齐 Find 深色；别名联想浮动下拉不撑布局；打标与去标单选当前标签为带 × 芯片；打标页不显示底部取消/打标，Enter 提交；去标页不显示别名与底部取消/去标，当前标签可检索后点叉号去除）
- Tray：WinForms 独立进程；默认 `Alt+L`（空/`none` 关热键）；Find 全入口单实例；关窗/失焦 Hide 保活；Run=`EverythingLabeled.Tray`；Host 仅同目录 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- exe 图标五分开：Tray/扩展纯标签 `el.ico`；Host `Assets/host.ico`（打开文件夹 E838 + 标签角标）；Service `Assets/service.ico`（服务器 E968 + 标签角标）；CLI `Assets/cli.ico`（命令提示符 E756 + 标签角标）；MCP `Assets/mcp.ico`（拼图/插件 E895 + 标签角标）→ `scripts/make-host-icon.ps1` · `scripts/make-service-icon.ps1` · `scripts/make-cli-icon.ps1` · `scripts/make-mcp-icon.ps1` · [Explorer](docs/explorer.md) · [Service 骨架](docs/service-skeleton.md) · [CLI](docs/cli.md) · [MCP](docs/mcp.md)
- Explorer 菜单图标：Segoe MDL2 字形生成多尺寸 `.ico`，**用 DIB 帧不用 PNG 帧**（PNG 帧在 GDI+ 消费端会解出乱码）；`GetIcon` 返绝对路径且包 try/catch；右键 `find` 用放大镜 `E721`；Host Find 标题栏保持 `E8B7`（与菜单刻意区分）→ [菜单图标存档](plans/2026-10-17-explorer-menu-icons.md) · [docs/explorer.md](docs/explorer.md)

## 待办
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)
- v1 安装器：规格已确认（[配置与安装](plans/2026-09-27-config-and-install.md)），实现计划尚未起草
- 本机验收：扩展重新 `npm run build` 并「重新加载」后，Popup 打开/切换模式后焦点在对应文本框；打标联想默认高亮第一项，↑/↓ 切换，Enter 直接打标 → [docs/browser-extension.md](docs/browser-extension.md)
- 本机验收 Host 去标：单选点叉号去除；检索只缩小当前标签；窗口无别名、取消、去标按钮 → [docs/explorer.md](docs/explorer.md)

已完成记录 → [plans/done.md](plans/done.md)

## 风险
- 查询「必须快」依赖缓存与查询引擎；#10 默认 Memory 已合入，Redis 为可选项
- Explorer / 浏览器扩展联调依赖 Service 业务 API，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
本机验收：已将 AppData `previewLimit`/`suggestLimit` 设为 8；重启 Host 后确认 Find 预览与拼首联想均最多 8 条 → [docs/explorer.md](docs/explorer.md) · [docs/config.md](docs/config.md)
