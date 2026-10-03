# EverythingLabeled 待办事项列表

日期：2026-09-23  
状态：待逐项讨论  
已完成基线：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)（含 `TagRelation` 网状关系、`NameInitials`）
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- [CLI 规格](./2026-09-26-cli.md)
- [浏览器扩展规格](./2026-09-27-browser-extension.md)
- [Explorer 集成规格](./2026-09-27-explorer-integration.md)
- [MCP 规格](./2026-09-27-mcp.md)
- [配置与安装](./2026-09-27-config-and-install.md)

用法：指定编号开始讨论（例如「讨论 1」）。一项确认后可再落盘为独立 `plans/*.md`，再进入下一项。

---

## A. 设计（写 `plans/`）

| # | 事项 | 状态 |
|---|---|---|
| 1 | **HTTP API 契约** — `/v1` 路由、JSON、错误码、鉴权头（含关系 CRUD / `Seq` 重排 / `neighbors` / `relations/batch` / 性能边界） | [已确认](./2026-09-26-http-api.md) |
| 2 | **查询语言细则** — 分词、`OR`/`-`、规范化、缓存键、**含子孙展开**（`expand=child`；默认 depth=8 / 512 tags） | [已确认](./2026-09-26-query-language.md) |
| 3 | **CLI 规格** — 命令集、读 config | [已确认](./2026-09-26-cli.md)（[审核记录](./confirmed/2026-09-26-cli-review.md)） |
| 4 | **浏览器扩展规格** — 打标当前页、按标签打开、服务发现 | [已确认](./2026-09-27-browser-extension.md)（[审核记录](./confirmed/2026-09-27-browser-extension-review.md)） |
| 5 | **Explorer 集成规格** — 右键、路径→URI、调用的 API | [已确认](./2026-09-27-explorer-integration.md)（[审核记录](./confirmed/2026-09-27-explorer-integration-review.md)） |
| 6 | **MCP 规格** — stdio Tools、与 CLI 同级、`IElApi` 预留 HTTP MCP | [已确认](./2026-09-27-mcp.md)（[审核记录](./confirmed/2026-09-27-mcp-review.md)） |
| 7 | **配置与安装** — Windows 安装/开机启动、端口、首次 token | [已确认](./2026-09-27-config-and-install.md)（[审核记录](./confirmed/2026-09-27-config-and-install-review.md)） |

## B. 实现（按依赖）

| # | 事项 | 状态 |
|---|---|---|
| 8 | **Service 骨架** — 解决方案、SQLite 迁移、Minimal API、本机 token | [已确认](./2026-09-27-service-skeleton.md)（[审核记录](./confirmed/2026-09-27-service-skeleton-review.md)）；[已实现](../docs/service-skeleton.md) |
| 9 | **CRUD + 查询解析** — 标签/资源/绑定/**关系**与查询引擎 | [已确认规格](./2026-09-29-crud-and-query.md)（[审核记录](./confirmed/2026-09-29-crud-and-query-review.md)）；[实现计划](./2026-10-03-crud-and-query-impl.md)；待开工 |
| 10 | **查询缓存** — `ITagQueryCache`（Memory 默认 + Redis）；须遵守 [CRUD §2.3](./2026-09-29-crud-and-query.md)（可配 SizeLimit/MaxEntrySize、键索引清前缀、绝对 TTL、singleflight 等） | 待讨论 |
| 11 | **CLI `el`** — 含共享 `EverythingLabeled.Client`（供 MCP / Explorer 复用） | 待讨论 |
| 12 | **MCP stdio host** — `Mcp.Tools` + stdio；依赖 Client / Service | 待讨论 |
| 13 | **浏览器扩展（Windows 联调）** | 待讨论 |
| 14 | **Explorer 上下文菜单包** | 待讨论 |

## C. 明确延后（不进 v1 优先队列）

| # | 事项 | 状态 |
|---|---|---|
| 15 | 多机同步协议 | 延后 |
| 16 | 托盘壳、文件跟标、全盘索引 | 延后 |
| 17 | HTTP MCP（Service 内 `MapMcp` + InProc `IElApi`） | 延后（边界见 [MCP 规格 §7](./2026-09-27-mcp.md)） |
