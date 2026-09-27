# EverythingLabeled Service 骨架规格

日期：2026-09-27  
状态：已确认（v1 实现基线；含 2026-09-27 grill Round 1–3 修订）  
审核记录：[Service 骨架讨论确认](./confirmed/2026-09-27-service-skeleton-review.md)  
关联：

- [本地守护进程架构](./2026-09-22-local-daemon-architecture.md)
- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [配置与安装](./2026-09-27-config-and-install.md)
- [路线图 backlog #8](./2026-09-23-roadmap-backlog.md)

## 1. 设计摘要

- **范围**：可运行的本机守护进程骨架——解决方案、SQLite 全量初始迁移、Minimal API 管道、本机 Bearer、`GET /v1/health`；**无**业务 CRUD / 查询引擎 / 缓存实装。
- **工程**：仅 `EverythingLabeled.Service` + `EverythingLabeled.Service.Tests`；不预建 Client / Core / CLI；`Nullable` + `TreatWarningsAsErrors`。
- **栈**：.NET 8、EF Core + 官方 Migrations、Serilog（Console + 滚动文件 + 访问日志）、Kestrel 仅绑 `127.0.0.1`。
- **数据**：`%AppData%/EverythingLabeled/data/el.db`；启动 `Migrate()`；按[数据模型](./2026-09-23-data-model.md)一次建齐表与索引。
- **配置**：与[配置与安装](./2026-09-27-config-and-install.md)一致——`config.json`（token/`baseUrl`）、Mutex、缺文件引导、非法不覆盖。
- **不加** CORS；失败退出码一律非 0（不承诺稳定码表）。

## 2. 解决方案与目录

```text
EverythingLabeled.sln
├── src/
│   └── EverythingLabeled.Service/
│       ├── EverythingLabeled.Service.csproj   # net8.0 Web SDK
│       ├── Program.cs
│       ├── appsettings.json                  # 非密钥（Cache、Serilog 等）
│       ├── Hosting/                          # Mutex、config 引导、路径
│       ├── Data/                             # DbContext、Entities、Migrations
│       ├── Auth/                             # Bearer 常量时间比较
│       ├── Http/                             # 信封、health
│       └── Logging/                          # Serilog 装配
└── tests/
    └── EverythingLabeled.Service.Tests/
```

- 单项目内按职责分文件夹（不拆 Infrastructure / Application 多项目）。
- 发布逻辑名：`EverythingLabeled.Service.exe`（见配置与安装）；开发态 `dotnet run`。
- `appsettings.json` **不含** token；与 `%AppData%/EverythingLabeled/config.json` 职责分离。
- csproj：`<Nullable>enable</Nullable>`、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`。

## 3. 启动流水线

失败即非 0 退出（已有本实例除外）：

1. **解析绝对路径**（不依赖 cwd）  
   - 默认应用数据根：`%AppData%/EverythingLabeled/`  
   - 若设置 **`EL_APPDATA`**：以其为应用数据根（等价替换上述目录；其下仍为 `config.json`、`data/`、`logs/`）  
   - 开发与测试用此变量隔离，避免污染真用户目录
2. **命名 Mutex**（每用户会话）  
   - 默认名：`Local\EverythingLabeled.Service`  
   - 测试可经 **`EL_MUTEX_NAME`** 覆盖（并行/CI 隔离）；未设置则用默认名  
   - 已有本实例 → **安静 exit 0**  
   - 拿锁后再做 config 引导
3. **config**（行为同[配置与安装 §3.1 / §4](./2026-09-27-config-and-install.md)）  
   - 不存在 → 探端口 5123–5132、生成 64 hex token、原子写、建 `data/` / `logs/`  
   - 存在但非法 → 失败，不重写  
   - 合法 → 读 `baseUrl` / `token`；**不热加载**
4. **Serilog** → Console + 滚动文件 → `logs/`（**按日**滚动，保留 **14** 天）；**禁止**记录 token / Bearer  
   - 另启 **HTTP 访问日志**（Information）：方法、路径、状态码、耗时；**永不**记录 `Authorization` 头
5. **Kestrel** 仅 `127.0.0.1:{port}`（来自通过校验的 `baseUrl`）  
   - 端口被他占 → 非 0 + 日志；**不改** config
   - 启动失败（非法 config / 端口 / 迁库 / 其它）→ **一律非 0**；**不**承诺稳定 exit code 表（安装器靠日志与 health）
6. **EF**：打开 `el.db` → 连接设置 **WAL** + **`busy_timeout=5000`** → `Database.Migrate()` → 确保 `Meta` 含 `data_version`（无则插入 `"0"`）  
   - **`Migrate()` 失败**：非 0 退出 + 日志；**保留**半截 `el.db`（不自动删除或改名）；排障后可手删再启
7. **管道**：统一信封 → Bearer（`GET /v1/health` 豁免）→ **无 CORS** → 映射 health
8. 开始监听（安装器轮询 health 的契约不变）

**优雅停止**：依赖 ASP.NET Generic Host / 控制台生命周期（Ctrl+C、进程关闭等）；**不**提供 `POST /v1/shutdown` 或命名 Event。安装器「先优雅、超时再强杀」的触发方式留给安装器实现。

**环境变量（#8）**

| 变量 | 作用 |
|---|---|
| `EL_APPDATA` | 可选；应用数据根（含 config / data / logs） |
| `EL_MUTEX_NAME` | 可选；覆盖 Mutex 名（测试用） |

## 4. 数据层

- `ElDbContext` + 实体对齐数据模型：Tag（含 `NameInitials` / `NameNormalized`）、TagAlias（含 `AliasNormalized`）、Resource、ResourceTag、TagRelation、Meta。
- **一次初始 Migration** 建齐表与[数据模型 §3](./2026-09-23-data-model.md)索引。
- 连接：启用 **WAL**；`busy_timeout = 5000`（毫秒）。
- SQLite 若不支持 `Kind=child` 的部分唯一索引：Migration 用普通 `(ToTagId, Kind)` 索引；**单父 / 成环**等应用层约束留给 #9 写路径（本骨架只保证列与可落库约束）。
- #8 **不**实现简拼算法、别名字符集校验、成环检测。

## 5. HTTP：信封、鉴权、health

### 5.1 信封

- 所有 `/v1` 响应（含 **404**、401、500）：`{ status, message, data }`；与 HTTP 状态同构（见[HTTP API §2.2](./2026-09-26-http-api.md)）。
- JSON：**camelCase**。
- 未处理异常 → `500` + `data.code: internal_error`；不使用 ProblemDetails 默认形状。
- 未知 `/v1/...`：**先鉴权**，再返回信封化 `404`（建议 `data.code: not_found`）；#8 **不**挂业务路由、不引入非正式探测端点。

### 5.2 鉴权

- 除 `GET /v1/health` 外：`Authorization: Bearer <token>`。
- 与 config token **常量时间**比较。
- 失败 → `401`，`data.code: unauthorized`。

### 5.3 Health

免鉴权。在既有契约上扩展 `dataVersion`：

```json
{
  "status": "ok",
  "message": null,
  "data": { "health": "ok", "dataVersion": 0 }
}
```

保留 `health` 字段；DB 不可用应在启动迁库阶段已失败，运行中 health 以 `ok` + 版本为主。

**`dataVersion` 类型**：对外 JSON **一律 number**（从 Meta 字符串 parse；缺行则插入 `"0"`；若值非整数 → **启动失败**，不静默当 0）。与[HTTP API](./2026-09-26-http-api.md) 写回执示例一致。

## 6. 配置键（服务端非密钥）

`appsettings.json` 示例（安装升级时始终用新包覆盖）：

```json
{
  "Cache": { "Provider": "Memory" },
  "Serilog": { "MinimumLevel": "Information" }
}
```

`ITagQueryCache` 实装属 #10；#8 只预留配置键，可不注册缓存实现。

## 7. 测试

`EverythingLabeled.Service.Tests` 集成测试（临时 `EL_APPDATA` + 唯一 `EL_MUTEX_NAME` + 隔离端口）：

| 用例 | 期望 |
|---|---|
| 无 config 引导 | 生成合法 config + 可启动 |
| 非法 config | 失败且不覆盖 |
| `GET /v1/health` | 免鉴权；`health` + **number** `dataVersion` |
| 无/错 Bearer 访问需鉴权路径 | `401` + `unauthorized` |
| 迁库 | `el.db` 存在且可读 `data_version` |
| 第二实例（可选） | 同 Mutex 下第二进程 exit 0 |

不测业务 CRUD。

## 8. 验收清单

- [ ] sln：`Service` + `Service.Tests`；TFM `net8.0`
- [ ] 无 config 引导（5123–5132、64 hex、原子写）；非法 config 失败不覆盖
- [ ] Mutex 名 `Local\EverythingLabeled.Service`；第二实例 exit 0；端口他占非 0 且 config 不变
- [ ] 仅绑 `127.0.0.1`；WAL + busy_timeout；`Migrate()`；`data/el.db` 全表+索引；`data_version` 初始存在
- [ ] `GET /v1/health` 免鉴权，信封含 `health:"ok"` + `dataVersion`（与 HTTP API 一致）
- [ ] 无/错 Bearer → `401` + `unauthorized`（常量时间比较）；未知 `/v1` 鉴权通过后信封化 `404`
- [ ] Serilog → `logs/`（按日、保留 14 天）+ 访问日志；日志无 token / Authorization
- [ ] 集成测试覆盖 §7；`Nullable` + `TreatWarningsAsErrors`
- [ ] 无 CORS 中间件

## 9. v1 本项非目标

- 业务 CRUD / 查询引擎 / `ITagQueryCache` 实现（#9 / #10）
- CLI、Client、MCP、Explorer、浏览器扩展、安装器框架选型
- Windows Service（SCM）、托盘、config 热加载、DPAPI
- Redis 实装（配置键可预留）
- 全量路由 stub；正式收录非正式 `auth/check` / shutdown 端点
- 显式停机 IPC（命名 Event 等）；安装器如何发 Ctrl+C 级信号留给安装器实现
- CORS；稳定进程退出码表；Swagger/OpenAPI UI（契约以 `plans/` 为准）

## 10. 交叉引用

| 主题 | 文档 |
|---|---|
| 进程形态 / 路径 / token / Mutex | [配置与安装](./2026-09-27-config-and-install.md) |
| 表与索引 | [数据模型](./2026-09-23-data-model.md) |
| 信封 / Bearer / health 基线 | [HTTP API](./2026-09-26-http-api.md) |
| 后续 CRUD | backlog **#9** |
| 查询缓存 | backlog **#10** |
