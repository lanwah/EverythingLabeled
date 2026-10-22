# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线与 #8–#14 / Tray / Find UI / 扩展 `#121`–`#122` 已合入 `master` 并推送；公有仓库 https://github.com/lanwah/EverythingLabeled → [骨架](plans/2026-09-27-service-skeleton.md) · [CRUD](plans/2026-09-29-crud-and-query.md) · [CLI](docs/cli.md) · [MCP](docs/mcp.md) · [扩展](docs/browser-extension.md) · [Explorer](docs/explorer.md)
- Explorer / Tray 本机验收通过：稀疏包 `0.1.9.0`、Host 四页深色皮肤、Tray `Alt+L`；Find Hide 保活 + 管道 Activate（回写 HWND，Tray/Host 双端抢前台；Notepad 占前台时 `Alt+L` 抢焦 8/8）。托盘右键「重新启动」结束全部 ExplorerHost 后冷启动 Find，避免服务晚于查找进程起来时横幅停在「服务未运行」。打标/去标/看标签/管理标签与 Find 一致：点击窗口外失焦自动关闭（Find 仍为 Hide）。打标页底部「取消」「打标」已隐藏，别名框 Enter 直接提交。去标页已按设计稿隐藏别名与底部「取消」「去标」，单选当前标签可检索后点叉号去除 → [docs/explorer.md](docs/explorer.md) · [Tray 规格](plans/2026-10-07-tray-find-hotkey.md) · [设计稿](design/explorer-ui/untag.html)
- Find：末词 suggest + 实时 preview、芯片输入；多标签 AND/OR 由 `config.json` 的 `findTagLogic`（`and`/`or`）配置、界面不展示；结果行 kind 图标 + 名称后资源标签胶囊（preview API `tags`）；无芯片时拼首联想底部显示「建议 N 条」；底部仅预览条数（无「清空标签」「打开」）；有芯片时输入框右侧清除图标；回车/双击结果行打开 → [Find UX](plans/confirmed/2026-10-07-find-ux-review.md) · [设计稿](design/explorer-ui/find.html) · [docs/explorer.md](docs/explorer.md)
- 浏览器扩展 0.1.3：Popup 固定 600px、无外层滚动条；标题单独一行，第二行 switch +「打标模式 / 查找模式」与管理按钮（默认打标）；查找页命中区吃满剩余高度并可按预览条数显示；拼首联想跟下方可用空间；打标 suggest 默认首项 + ↑/↓/Enter；查找 ↑/↓/空输入 Enter 对齐 Find → [docs/browser-extension.md](docs/browser-extension.md)
- 扩展 Popup 设计稿：标题一行 + 第二行 switch/按钮；无打标/去标与清空/打开所选；Enter 打标、芯片 × 去标；查找结果占位 + 拼首联想浮动、无勾选、双击打开；清除图标；设置「预览资源类型」存 `chrome.storage.local`（默认仅链接）→ [ext-page](design/explorer-ui/ext-page.html) · [ext-options](design/explorer-ui/ext-options.html)
- 管理标签已落地（Host `library`，列表每页 50）。管理资源：按标签查找后删除或替换；类型按新地址预选可改；芯片查询与 Find 相同（末词 suggest 含简拼，Enter 落联想）；芯片区清空图标对齐 Find（无边框）；空状态提示居中。稀疏包 `0.1.10.0`。右键只少新增项时查 comhost 是否过期（`CoCreate` `0x80040111`）→ [存档](plans/2026-10-20-explorer-comhost-stale.md) · [docs/explorer.md](docs/explorer.md)

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
- Find UI：芯片输入；多标签逻辑读 `findTagLogic`（默认 `and`），不在 UI 切换；Find 无边框深色标题栏；Enter 落芯片 → [芯片审核](plans/confirmed/2026-10-07-find-chip-and-or-review.md)（UI 切换已改为配置项）
- Explorer Host 四页皮肤：以 [design/explorer-ui/](design/explorer-ui/index.html) 为视觉基线（打标/去标/看标签对齐 Find 深色；别名联想浮动下拉不撑布局；打标与去标单选当前标签为带 × 芯片；打标页不显示底部取消/打标，Enter 提交；去标页不显示别名与底部取消/去标，当前标签可检索后点叉号去除）
- Tray：WinForms 独立进程；默认 `Alt+L`（空/`none` 关热键）；Find 全入口单实例；关窗/失焦 Hide 保活；Run=`EverythingLabeled.Tray`；Host 仅同目录 → [Tray 规格](plans/2026-10-07-tray-find-hotkey.md)
- Service exe 图标：`Assets/service.ico`（服务器 E968 + 标签角标 E8EC，与 Tray/Host 纯标签 `el.ico` 区分）→ `scripts/make-service-icon.ps1` · [Service 骨架](docs/service-skeleton.md)
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
本机验收扩展 Popup：重新 `npm run build` 并「重新加载」后，打开默认「打标模式」且别名框获焦，切「查找模式」后查询框获焦；查找结果区更高；↑/↓ 与空输入 Enter 打开仍可用 → [docs/browser-extension.md](docs/browser-extension.md)
