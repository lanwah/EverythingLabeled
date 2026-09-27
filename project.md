# 项目看板

## 项目目标
本机「任意对象可打标签、按标签快速找回」的标签服务；v1 以 Windows 本地守护进程为唯一数据入口，CLI / 浏览器扩展 / Explorer / MCP 均为薄客户端。

## 当前状态
- 设计基线已确认：架构、数据模型、HTTP API、查询语言、CLI、扩展、Explorer、MCP、配置安装（见 `plans/`）
- v1 实现项 #8–#14 均「待讨论」，仓库尚无业务源码 / 解决方案骨架
- 已安装项目看板规则（`.cursor/rules/project-board.mdc`）与本文件
- 延后明确不进 v1：多机同步、托盘/跟标/全盘索引、HTTP MCP

## 关键决策
- 单体本地守护进程 + SQLite；客户端只走 HTTP（`127.0.0.1` + Bearer）→ [架构](plans/2026-09-22-local-daemon-architecture.md)
- 技术栈 .NET（C#）；Windows 优先，Service/CLI 避开 Windows-only API
- 标签：扁平主名 + 可选命名空间；`TagRelation` 网状（`child`/`related`）；查询默认 AND，支持 OR / `-tag` → [数据模型](plans/2026-09-23-data-model.md)
- 文件身份仅路径；改名/移动不自动跟标
- 查询缓存可插拔（v1 默认 Memory，可配 Redis）→ [架构 §5](plans/2026-09-22-local-daemon-architecture.md)
- MCP v1 仅 stdio；HTTP MCP 延后 → [MCP](plans/2026-09-27-mcp.md)

## 待办
- #8 Service 骨架（解决方案、SQLite 迁移、Minimal API、本机 token）
- #9 CRUD + 查询解析（标签/资源/绑定/关系与查询引擎）
- #10 查询缓存 `ITagQueryCache`
- #11 CLI `el` + 共享 `EverythingLabeled.Client`
- #12 MCP stdio host
- #13 浏览器扩展（Windows 联调）
- #14 Explorer 上下文菜单包
- 总览与编号约定见 [roadmap backlog](plans/2026-09-23-roadmap-backlog.md)

## 风险
- 设计已齐、实现未开：讨论与编码间隙可能导致规格与落地漂移
- 查询「必须快」依赖缓存与查询引擎一次做对；#9/#10 耦合紧
- Explorer / 浏览器扩展联调依赖 Service 先可用，易成进度瓶颈
- 别名字符集、简拼、关系 `Seq` 等细则多，实现时易漏边界（以已确认 plans 为准）

## 下一步
讨论并确认 #8 Service 骨架（解决方案结构、SQLite 迁移、Minimal API、本机 token），达成一致后落盘 `plans/` 再实现 — 入口 [roadmap #8](plans/2026-09-23-roadmap-backlog.md)
