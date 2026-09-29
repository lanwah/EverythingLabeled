# CRUD + 查询解析（#9）讨论确认记录

日期：2026-09-29  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）+ 设计 §1–§5 逐节确认  
已落盘同步：

- [CRUD + 查询解析规格](../2026-09-29-crud-and-query.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#9 已确认规格）
- [项目看板](../../project.md)

---

## A. 主线澄清

| # | 议题 | 确认 |
|---|---|---|
| Q1 | #9 范围与 #10 边界 | **A** — 全量业务 API + 查询引擎；`ITagQueryCache` 空实现；#10 换 Memory/Redis |
| Q2 | Service 内部分层 | **A** — 单项目 + `Services/` 用例层；endpoint 薄 |
| Q3 | 查询冷路径 | **A** — EF Core LINQ 优先；难点可局部 SQL / `FromSql` |
| Q4 | `data_version` | **A+i** — 集中 `CommitAsync`；仅有真实持久化变更才 bump |
| Q5 | `NameInitials` | **A** — NuGet 拼音库 + `INameInitialsGenerator`；用例表锁行为 |
| Q6 | 测试策略 | **A** — 单元 + Service/内存 SQLite + 关键 HTTP 集成 |
| Q7 | 查询模块拆分 | **A** — Parser / Expander / Executor + 独立 `NormalizeQuery` |

## B. 设计节确认

| 节 | 内容 | 确认 |
|---|---|---|
| §1 | 范围与边界 / 非目标 / 验收口径 | ok |
| §2 | 目录与组件 / DI / 横切 | ok |
| §3 | 写路径与关系约束 | ok |
| §4 | 查询读路径与缓存桩 | ok |
| §5 | 错误映射、测试与验收 | ok |

## 实现入口

规格已确认。下一动作：按 [CRUD + 查询解析规格](../2026-09-29-crud-and-query.md) 起草实现计划（或用户确认后直接按 §8 顺序开工）。
