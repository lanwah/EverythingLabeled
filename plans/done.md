# 已完成记录

按时间正序追加。每行一条：

`YYYY-MM-DD — 一句话结果`（可选：链到相关 `plans/*` 或 PR）

- 2026-09-22 — 确认本地守护进程架构基线 → [架构](./2026-09-22-local-daemon-architecture.md)
- 2026-09-23 — 确认数据模型（含 TagRelation、NameInitials）→ [数据模型](./2026-09-23-data-model.md)
- 2026-09-26 — 确认 HTTP API 契约 → [HTTP API](./2026-09-26-http-api.md)
- 2026-09-26 — 确认查询语言细则 → [查询语言](./2026-09-26-query-language.md)
- 2026-09-26 — 确认 CLI 规格 → [CLI](./2026-09-26-cli.md)
- 2026-09-27 — 确认浏览器扩展规格 → [浏览器扩展](./2026-09-27-browser-extension.md)
- 2026-09-27 — 确认 Explorer 集成规格 → [Explorer](./2026-09-27-explorer-integration.md)
- 2026-09-27 — 确认 MCP 规格（stdio；HTTP MCP 延后）→ [MCP](./2026-09-27-mcp.md)
- 2026-09-27 — 确认配置与安装规格 → [配置与安装](./2026-09-27-config-and-install.md)
- 2026-09-27 — 安装项目看板规则与根目录 `project.md`（含闭环回写 / `plans/done.md`）
- 2026-09-27 — 确认 #8 Service 骨架规格 → [Service 骨架](./2026-09-27-service-skeleton.md)
- 2026-09-27 — grill Round 1 修订 Service 骨架（Mutex/WAL/信封404/日志保留/health dataVersion）并同步 HTTP API
- 2026-09-27 — grill Round 2 修订 Service 骨架（`EL_APPDATA` / `EL_MUTEX_NAME` / dataVersion number / 迁库失败保留 db）
- 2026-09-27 — grill Round 3 修订 Service 骨架（无 CORS / 访问日志 / Nullable+WAsE / 退出码不钉表）
- 2026-09-27 — 实现 #8 Service 骨架（sln、EF 迁库、health/鉴权、Mutex/config 引导、集成测试 8/8）→ [Service 骨架](./2026-09-27-service-skeleton.md)
- 2026-09-27 — 撰写 #8 实现说明文档 → [docs/service-skeleton.md](../docs/service-skeleton.md)
- 2026-09-29 — 确认 #9 CRUD + 查询解析实现规格 → [CRUD + 查询解析](./2026-09-29-crud-and-query.md)
- 2026-09-29 — grill Round 1–4 修订 #9 规格（dirty/Revision/缓存键/ServiceResult/性能边界等）并展开审核问答 → [审核记录](./confirmed/2026-09-29-crud-and-query-review.md)
- 2026-09-29 — grill Round 5 钉死缓存写后正确性（原子 `data_version+1`；Set 前 version 复核、变则不写入）→ [CRUD + 查询解析](./2026-09-29-crud-and-query.md) · [审核 §G](./confirmed/2026-09-29-crud-and-query-review.md)
- 2026-09-29 — grill Round 6 钉死 #10 防撑爆硬约束（SizeLimit；写后异步清旧 version 前缀；suggest/preview 短 TTL）→ [CRUD §2.3](./2026-09-29-crud-and-query.md) · [审核 §H](./confirmed/2026-09-29-crud-and-query-review.md)
- 2026-09-29 — grill Round 7 缓存专题（合法空可缓存；singleflight；键索引前缀删；可配 MaxEntrySize；Memory 防御拷贝；仅成功 Set；绝对 TTL；#9 Commit 接线 InvalidatePrefix）→ [审核 §I](./confirmed/2026-09-29-crud-and-query-review.md)
- 2026-09-29 — grill Round 8 缓存专题收束（Cache:* 可配默认；PostEviction 同步索引；record DTO；日志计数；清理失败不翻写）→ [审核 §J](./confirmed/2026-09-29-crud-and-query-review.md) · [CRUD §2.3](./2026-09-29-crud-and-query.md)
