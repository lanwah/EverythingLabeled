# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- v1 设计基线已齐；#8 Service 骨架与 #9 CRUD + 查询解析已合入 `master`（117 测通过）→ [骨架规格](plans/2026-09-27-service-skeleton.md) · [CRUD 规格](plans/2026-09-29-crud-and-query.md) · [实现计划](plans/2026-10-03-crud-and-query-impl.md) · [说明](docs/crud-and-query.md)
- #10–#14 仍待讨论/实现；延后：多机同步、托盘/跟标、全盘索引、HTTP MCP

## 关键决策
- 单体本地守护进程 + SQLite；客户端只走 HTTP（`127.0.0.1` + Bearer）→ [架构](plans/2026-09-22-local-daemon-architecture.md)
- 技术栈 .NET 8（C#）；Windows 优先，Service/CLI 避开 Windows-only API
- 标签：扁平主名 + 可选命名空间；`TagRelation` 网状（`child`/`related`）；查询默认 AND，支持 OR / `-tag` → [数据模型](plans/2026-09-23-data-model.md)
- 文件身份仅路径；改名/移动不自动跟标
- 查询缓存可插拔（v1 默认 Memory，可配 Redis）→ [架构 §5](plans/2026-09-22-local-daemon-architecture.md)
- MCP v1 仅 stdio；HTTP MCP 延后 → [MCP](plans/2026-09-27-mcp.md)
- Service 骨架：单项目文件夹分层、EF Core Migrations、`el.db`、Serilog、health+鉴权最小集 → [Service 骨架](plans/2026-09-27-service-skeleton.md)
- #9：原子 bump + 写后异步 InvalidatePrefix；查询三分流水线；Null 缓存；Set 前复核；写回执扁平 dataVersion；suggest 无 offset；Cache:* 键名已钉；kind 与 URI 须兼容 → [CRUD §11](plans/2026-09-29-crud-and-query.md) · [实现计划](plans/2026-10-03-crud-and-query-impl.md)

## 待办
- [ ] #10 查询缓存 `ITagQueryCache`
- [ ] #11 CLI `el` + 共享 `EverythingLabeled.Client`
- [ ] #12 MCP stdio host
- [ ] #13 浏览器扩展（Windows 联调）
- [ ] #14 Explorer 上下文菜单包
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)

已完成记录 → [plans/done.md](plans/done.md)

## 风险
- 查询「必须快」依赖缓存与查询引擎一次做对；#9 冷路径已落地，#10 接可插拔缓存
- Explorer / 浏览器扩展联调依赖 Service 业务 API，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
与用户确认是否按 backlog 开工 #10 查询缓存 `ITagQueryCache`（规格讨论 / 起草实现计划）→ [roadmap](plans/2026-09-23-roadmap-backlog.md) · [CRUD §2.3](plans/2026-09-29-crud-and-query.md)
