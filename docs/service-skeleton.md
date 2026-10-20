# Service 骨架（#8）实现说明

日期：2026-09-27  
状态：已实现  
规格：[plans/2026-09-27-service-skeleton.md](../plans/2026-09-27-service-skeleton.md)

本机守护进程的最小可运行骨架：解决方案、SQLite 全量初始迁移、Minimal API、本机 Bearer、`GET /v1/health`。不含业务 CRUD、查询引擎与缓存实装。

## 1. 解决方案布局

```text
EverythingLabeled.sln
├── src/EverythingLabeled.Service/
│   ├── Program.cs                 # 启动流水线入口
│   ├── appsettings.json           # 非密钥（Cache、Serilog 等）
│   ├── Hosting/                   # 路径、Mutex、config 引导
│   ├── Data/                      # DbContext、实体、Migrations
│   ├── Auth/                      # Bearer 常量时间比较
│   ├── Http/                      # 信封、health、异常与 404
│   └── Logging/                   # Serilog 装配
└── tests/EverythingLabeled.Service.Tests/   # 进程级集成测试
```

- TFM：`net8.0`；`Nullable` + `TreatWarningsAsErrors`
- 发布逻辑名：`EverythingLabeled.Service`（开发态 `dotnet run`）
- `appsettings.json` **不含** token；与 `%AppData%/EverythingLabeled/config.json` 职责分离

## 2. 本地运行

```powershell
# 仓库根目录（或 build.bat / .\build.ps1，见 [文档首页](./README.md#编译)）
dotnet build EverythingLabeled.sln
dotnet run --project src/EverythingLabeled.Service
```

首次无 config 时会自动：

1. 在 `5123–5132` 探测空闲端口并生成 64 位小写 hex token  
2. 原子写入 `%AppData%/EverythingLabeled/config.json`  
3. 创建 `data/`、`logs/`，迁移 `data/el.db`  
4. 仅绑定 `http://127.0.0.1:{port}`

开发/测试请用环境变量隔离，避免写入真用户目录：

```powershell
$env:EL_APPDATA = "D:\tmp\el-dev"
$env:EL_MUTEX_NAME = "Local\EverythingLabeled.Service.Dev"
dotnet run --project src/EverythingLabeled.Service
```

探活（免鉴权）：

```powershell
Invoke-RestMethod http://127.0.0.1:5123/v1/health
# → status=ok, data.health=ok, data.dataVersion=<number>
```

## 3. 环境变量与路径

| 变量 | 作用 |
|---|---|
| `EL_APPDATA` | 可选；应用数据根（其下仍为 `config.json`、`data/`、`logs/`） |
| `EL_MUTEX_NAME` | 可选；覆盖命名 Mutex（默认 `Local\EverythingLabeled.Service`） |

默认应用数据根：`%AppData%/EverythingLabeled/`（不依赖进程 cwd）。

| 路径 | 用途 |
|---|---|
| `{root}/config.json` | `baseUrl` + `token` 等（客户端与 Service 共用）；键表见 [本机配置](./config.md) |
| `{root}/data/el.db` | SQLite 主库 |
| `{root}/data/el.db-wal` | WAL 预写日志（启用 WAL 时出现；可能为 0 字节） |
| `{root}/data/el.db-shm` | WAL 共享内存索引（启用 WAL 时出现） |
| `{root}/logs/service-*.log` | Serilog 按日滚动，保留 14 天 |

`data/` 下 **`el.db` 与 `-wal` / `-shm` 是一套库**，不是重复备份。Service 在迁库前会设 `journal_mode=WAL`（见 §4 第 6 步）。备份或迁移时请**先停 Service**，再整目录复制 `data/`；勿在 Service 写入时单独删除 `-wal` / `-shm`。

## 4. 启动行为摘要

失败一律非 0 退出（**已有本实例除外 → 安静 exit 0**）。不承诺稳定退出码表。

1. 解析绝对路径（`EL_APPDATA` 或默认 AppData）  
2. 获取命名 Mutex；第二实例 exit 0  
3. config：不存在则引导；存在但非法则失败且**不覆盖**  
4. Serilog（Console + 文件）；禁止记录 token / `Authorization`  
5. Kestrel 仅 `127.0.0.1:{port}`；端口被占 → 非 0，**不改** config  
6. SQLite：`WAL` + `busy_timeout=5000` → `Migrate()` → 确保 `Meta.data_version`（缺则 `"0"`；非整数则启动失败）  
7. 管道：异常信封 → 访问日志 → Bearer（health 豁免）→ `/v1` 404 信封 → health  
8. **无 CORS**；优雅停止依赖 Generic Host（Ctrl+C 等），无 shutdown API  

`baseUrl` / token 规则与 [配置与安装](../plans/2026-09-27-config-and-install.md) 一致（`http` + `127.0.0.1`、64 hex 等）。

## 5. HTTP（#8 范围）

统一信封（camelCase）：`{ status, message, data }`。`status:"ok"` ⇔ 2xx；错误 ⇔ 4xx/5xx。

| 场景 | 行为 |
|---|---|
| `GET /v1/health` | 免鉴权；`data`: `{ health: "ok", dataVersion: <number> }` |
| 其它 `/v1/...` 无/错 Bearer | `401`，`data.code: unauthorized`（常量时间比较） |
| 未知 `/v1/...`（鉴权通过后） | `404`，`data.code: not_found` |
| 未处理异常 | `500`，`data.code: internal_error`（非 ProblemDetails） |

#8 **不**挂业务路由。完整契约见 [HTTP API](../plans/2026-09-26-http-api.md)。

## 6. 数据层

- `ElDbContext` + 实体：Tag / TagAlias / Resource / ResourceTag / TagRelation / Meta  
- 一次初始 Migration 建齐表与索引（见 [数据模型](../plans/2026-09-23-data-model.md)）  
- SQLite 不支持 `Kind=child` 部分唯一索引时：普通 `(ToTagId, Kind)` 索引；单父/成环等留给 #9  
- #8 **不**实现简拼、别名字符集校验、成环检测  
- 迁库失败：非 0 + 日志；**保留**半截 `el.db`（不自动删除）  
- 设计时迁移：`Data/ElDbContextFactory`（避免 `dotnet ef` 误跑宿主）

新增迁移示例：

```powershell
dotnet ef migrations add <Name> --project src/EverythingLabeled.Service --output-dir Data/Migrations
```

## 7. 配置键（服务端非密钥）

`appsettings.json` 示例（安装升级时用新包覆盖）：

```json
{
  "Cache": { "Provider": "Memory" },
  "Serilog": { "MinimumLevel": "Information" }
}
```

`ITagQueryCache` 实装属 #10；#8 仅预留 `Cache:Provider`。

## 8. 测试

```powershell
dotnet test EverythingLabeled.sln
```

`EverythingLabeled.Service.Tests` 为**进程级**集成测试：临时 `EL_APPDATA` + 唯一 `EL_MUTEX_NAME` + 隔离端口。覆盖：

- 无 config 引导可启动  
- 非法 config 失败且不覆盖  
- health 免鉴权 + number `dataVersion`  
- 无/错 Bearer → `unauthorized`  
- 未知 `/v1` 鉴权后信封化 404  
- 迁库后 `el.db` 可读 `data_version`（WAL）  
- 同 Mutex 第二进程 exit 0  
- 端口他占非 0 且 config 不变  

注意：子进程若重定向标准输出，测试侧必须排空管道，否则 Serilog Console / `CloseAndFlush` 可能堵死导致假超时。

## 9. 本项非目标（指向后续）

| 能力 | backlog |
|---|---|
| 标签/资源/绑定/关系 CRUD + 查询引擎 | #9 |
| `ITagQueryCache` | #10 |
| CLI / Client / MCP / Explorer / 扩展 / 安装器 | #11–#14 等 |
| Windows Service（SCM）、托盘、config 热加载、DPAPI、CORS、Swagger UI | 非 v1 或另行规格 |

## 10. 交叉引用

| 主题 | 文档 |
|---|---|
| 实现规格（权威） | [Service 骨架规格](../plans/2026-09-27-service-skeleton.md) |
| 路径 / token / Mutex | [配置与安装](../plans/2026-09-27-config-and-install.md) |
| 表与索引 | [数据模型](../plans/2026-09-23-data-model.md) |
| 信封 / Bearer / health | [HTTP API](../plans/2026-09-26-http-api.md) |
| 编号总览 | [roadmap backlog](../plans/2026-09-23-roadmap-backlog.md) |
