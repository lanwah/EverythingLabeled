# EverythingLabeled 本地守护进程架构

日期：2026-09-22  
状态：已确认（v1 基线）

## 1. 背景与目标

EverythingLabeled 是一套「任意对象可打标签、按标签快速找回」的本地标签服务。  
v1 以本机守护进程为唯一数据入口，CLI、浏览器扩展、资源管理器集成均为薄客户端。

## 2. 已确认决策

| 项 | 选择 |
|---|---|
| 形态 | 本地后台服务 + 多端客户端 |
| 同步 | 先单机；模型与 API 预留；v1 不做同步实现 |
| v1 客户端 | Service API + CLI + 浏览器扩展 + Explorer 集成 |
| 平台 | Windows 优先；Service / CLI 避开 Windows-only API，便于日后多平台 |
| 标签 | 扁平主名 + 可选命名空间；多别名；未设别名时默认别名 = 标签名；**网状关系 `TagRelation`（`child` / `related`），父子与子排序由 `child` + `Seq` 表达**；**返回标签默认按 `Seq` 升序，未设置或相同时按 `CreatedAt` 升序** |
| 文件身份 | 仅路径；改名 / 移动不自动跟标 |
| 查询 | 默认多标签 AND；支持 OR 与排除（`-tag`） |
| 查询性能 | 必须快；提供建议 / 预览接口；缓存可插拔（内存 / Redis） |
| 技术栈 | .NET（C#） |
| 架构选型 | 方案 1：单体本地守护进程 + SQLite；客户端只走 HTTP |

## 3. 架构总览

```text
[CLI el] ──┐
[Explorer] ─┼── HTTP JSON + Bearer token ──► [EverythingLabeled.Service]
[浏览器扩展]┘                                      │
                                                   ├─ ITagQueryCache (Memory | Redis)
                                                   └─ SQLite
```

- 所有读写经 Service；客户端不直连 SQLite。
- 查询路径优先走缓存；未命中再查库。
- 写成功后递增 `data_version`，使旧缓存键自然失效。

## 4. 组件职责与通信

| 组件 | 职责 |
|---|---|
| `EverythingLabeled.Service` | Minimal API、查询解析、持久化、缓存、本机鉴权 |
| `el`（CLI） | 打标、查标、建议补全；只调 HTTP |
| Explorer 集成 | 右键打标 / 按标签查找；`IExplorerCommand` / 稀疏包，避免老式 in-proc Shell Extension |
| Browser extension | 当前页 URL 打标、按标签打开；localhost + token（日后可加 Native Messaging） |

- 监听：`127.0.0.1`（不暴露公网）。
- 端口与 token 写入 `%AppData%/EverythingLabeled/config.json`，供各客户端读取。
- API 前缀：`/v1/...`。

## 5. 查询缓存

### 5.1 目标

- 标签建议、查询预览、完整查询低延迟。
- 缓存实现可替换：默认进程内内存；可配置切换 Redis（为多实例 / 同步中心预留）。

### 5.2 抽象

- `ITagQueryCache`（可基于 `IDistributedCache` 薄封装）：
  - `Get` / `Set`
  - `InvalidateAll` / `InvalidatePrefix`（写路径可选调用）
- 配置：
  - `Cache:Provider = Memory | Redis`
  - `Cache:RedisConnection`（仅 Redis 需要）
- **v1 默认 `Memory`**，不依赖外部进程；Redis 实现保留并可用配置启用，安装不强制依赖 Redis。

### 5.3 读接口约定

| 意图 | 路径 | 行为 |
|---|---|---|
| 标签建议 | `GET /v1/tags/suggest?q=&limit=` | 按名称 / 别名前缀匹配；强缓存；供输入联想 |
| 查询预览 | `GET /v1/query/preview?q=&limit=` | 解析查询后返回少量命中（uri + 展示名）；可短 TTL |
| 完整查询 | `GET /v1/query?q=&offset=&limit=` | 分页完整结果；按页缓存 |

### 5.4 失效策略

- SQLite `Meta` 表维护单调 `data_version`；任意标签 / 别名 / 绑定 / **关系** 写成功后递增。
- 缓存键（query / preview）：
  `v{data_version}:{op}:{normalized_query}:{expand}:{maxDepth}:{expandedTagLimit}:{limit}:{offset}`  
  规范化与未展开占位见 [查询语言细则](./2026-09-26-query-language.md)。
- 写后不必逐键删除：version 前进即失效；可异步清理旧前缀。

### 5.5 未命中缓存时的库侧保障

- 别名全局唯一索引、绑定表索引、**关系表索引**，保证冷查询仍快。
- 建议接口走名称 / 别名前缀索引，避免全表扫描。

## 6. 安全与本机配置

- 鉴权：本机共享 Bearer token；无账号体系。
- 仅绑定回环地址。
- 同步阶段再引入用户身份与远程鉴权。

示例配置位置：`%AppData%/EverythingLabeled/config.json`

```json
{
  "baseUrl": "http://127.0.0.1:5123",
  "token": "<local-secret>"
}
```

服务端另含 `Cache:Provider` 等项（可用 `appsettings.json` 或同目录配置）。

## 7. 资源 URI 与多平台 / 同步预留

### 资源 URI

- 文件 / 目录：`file:///C:/path/to/item`（盘符大写、`/` 分隔；非根路径去掉末尾 `/`）。
- 网页：scheme / host 小写、省略默认端口、去掉 fragment。

### 多平台

- Service 与 CLI 使用可移植 .NET API。
- Explorer 集成为 Windows 专用工程；其它 OS 用对应 shell 集成替换。

### 同步预留（v1 不实现）

- 实体保留 `revision`、`updated_at`。
- 不做同步协议、冲突合并、账号。
- 文件类跨机仅按路径字符串同步元数据（与「只认路径」一致）。

## 8. v1 非目标

- 系统托盘壳
- 文件改名 / 移动自动跟标
- 多机同步实现
- Everything 式全盘索引 / USN 监视
- 强制依赖 Redis

## 9. 后续文档

- [数据模型](./2026-09-23-data-model.md)
- [HTTP API 契约](./2026-09-26-http-api.md)
- [查询语言细则](./2026-09-26-query-language.md)
- 各客户端实现计划（待开）
