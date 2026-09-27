# Service 骨架规格讨论确认记录

日期：2026-09-27  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）+ grill 审核 Round 1  
已落盘同步：

- [Service 骨架规格](../2026-09-27-service-skeleton.md)
- [HTTP API](../2026-09-26-http-api.md)（health `data` 含 `dataVersion`）
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#8 已确认）
- [项目看板](../../project.md)

对 @plans/2026-09-27-service-skeleton.md 的确认点如下。

---

## A. 主线澄清

| # | 议题 | 确认 |
|---|---|---|
| Q1 | 解决方案项目 | **A** — 仅 Service（+ Tests）；不预建 Client / Core |
| Q2 | 数据访问 | **A** — EF Core + 官方 Migrations |
| Q3 | 目标框架 | **A** — .NET 8（`net8.0`） |
| Q4 | API / 启动范围 | **A′** — 可运行最小集 + health 带 `dataVersion`；无业务 CRUD / 无全量 stub |
| Q5 | 日志 | **B** — Serilog：Console + 滚动文件 → AppData `logs/`；禁 token |
| Q6 | 库文件名 | **A** — `data/el.db` |
| Q7 | 初始迁移范围 | **B** — 按数据模型一次建齐全部表与索引 |
| Q8 | 测试 | **A** — 含 `Service.Tests` 集成测（引导 / health / 401 / 迁库） |
| 组织 | 项目内结构 | **方案 2** — 单项目 + Hosting / Data / Auth / Http / Logging 文件夹 |
| 设计 §1–§4 | 目录、启动流水线、数据/鉴权/信封、验收与非目标 | 用户逐段 **ok** / **可以** |

## B. Grill 审核 Round 1

| # | 议题 | 确认 |
|---|---|---|
| Q1 | health 与 `/v1/meta` 的 `dataVersion` | **A** — health 保留；回写 HTTP API |
| Q2 | 未知 `/v1` 的 404 | **A** — 一律信封化 |
| Q3 | Mutex 名 | **A** — `Local\EverythingLabeled.Service` |
| Q4 | 优雅停止 | **A** — 标准 host 生命周期；无 shutdown API |
| Q5 | Serilog 滚动 | **A** — 按日，保留 14 天 |
| Q6 | SQLite pragma | **A** — WAL + `busy_timeout=5000` |

## C. Grill 审核 Round 2

| # | 议题 | 确认 |
|---|---|---|
| Q7 | `EL_APPDATA` 语义 | **A** — 应用数据根（其下 config/data/logs） |
| Q8 | 测试隔离 Mutex | **A** — 生产默认名；测试可用 `EL_MUTEX_NAME` |
| Q9 | `dataVersion` JSON 类型 | **A** — 对外一律 number |
| Q10 | `Migrate()` 失败 | **A** — 非 0 退出；保留半截 db，不自动删 |

## D. Grill 审核 Round 3

| # | 议题 | 确认 |
|---|---|---|
| Q11 | 失败退出码 | **A** — 一律非 0；不钉稳定码表 |
| Q12 | CORS | **A** — #8 不加 |
| Q13 | HTTP 访问日志 | **A** — Information；不记 Authorization |
| Q14 | csproj 严格度 | **A** — Nullable + TreatWarningsAsErrors |

## 实现入口

Grill frontier 已空（见下轮收束确认）。实现：按 [Service 骨架规格](../2026-09-27-service-skeleton.md) 并通过 §8 验收清单。
