# EverythingLabeled

本机「任意对象可打标签、按标签快速找回」的标签服务。

v1 以 **Windows 本地守护进程** 为唯一数据入口；CLI、资源管理器右键、托盘热键、浏览器扩展与 MCP 都是薄客户端，经本机 HTTP + Bearer 访问，不直连数据库。

## 能做什么

- **打标 / 去标 / 看标签**：本地文件路径与网页 URL 均可挂标签
- **按标签查找**：默认 AND，支持 OR；末词联想与实时预览
- **多入口**：`el` CLI、Explorer 右键四菜单、托盘 `Alt+L` 打开 Find、Chrome/Edge 扩展、MCP（stdio）
- **标签模型**：扁平主名 + 可选命名空间；标签间可建 `child` / `related` 关系

## 架构一览

```text
  el (CLI) ─────────┐
  MCP stdio ────────┤
  Explorer Host ────┼──► HTTP + Bearer (127.0.0.1) ──► EverythingLabeled.Service
  浏览器扩展 ────────┘                                      ├─ SQLite (el.db)
                                                            └─ 可插拔查询缓存
```

详细组件关系、依赖与运行时协作见 [docs/README.md](docs/README.md)。

## 环境要求

| 用途 | 要求 |
|---|---|
| 编译 / 运行 .NET 工程 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 全量验收（右键菜单、Tray） | Windows 10/11 x64 |
| 浏览器扩展构建 | Node.js ≥ 20 |
| 查询缓存 Redis | 可选；默认 Memory，无需 Redis |

## 快速开始

### 1. 编译

```powershell
.\build.ps1
# 或
dotnet build EverythingLabeled.sln
```

### 2. 启动 Service

```powershell
dotnet run --project src/EverythingLabeled.Service
```

首次启动会在 `%APPDATA%\EverythingLabeled\` 写入 `config.json`（端口与 token）、创建 `data/el.db`，并监听 `http://127.0.0.1:{port}`。

探活：

```powershell
Invoke-RestMethod http://127.0.0.1:5123/v1/health
```

开发时可用环境变量隔离用户目录，避免写到真 AppData：

```powershell
$env:EL_APPDATA = "D:\tmp\el-dev"
$env:EL_MUTEX_NAME = "Local\EverythingLabeled.Service.Dev"
dotnet run --project src/EverythingLabeled.Service
```

### 3. 使用客户端

```powershell
# CLI
dotnet run --project src/EverythingLabeled.Cli -- --help

# Find 窗口（需 Service 已运行）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode find

# 托盘 + 热键（默认 Alt+L）
dotnet run --project src/EverythingLabeled.Tray
```

Explorer 右键菜单需注册稀疏包，见 [Explorer 文档](docs/explorer.md)。浏览器扩展见 [ext/](ext/) 与 [浏览器扩展文档](docs/browser-extension.md)。MCP 接入见 [MCP 文档](docs/mcp.md)。

## 文档

| 文档 | 说明 |
|---|---|
| [docs/README.md](docs/README.md) | 环境、架构、编译与分项索引 |
| [docs/cli.md](docs/cli.md) | CLI `el` |
| [docs/explorer.md](docs/explorer.md) | Explorer 右键、Host、Tray |
| [docs/mcp.md](docs/mcp.md) | MCP stdio |
| [docs/browser-extension.md](docs/browser-extension.md) | Chrome / Edge MV3 |
| [docs/crud-and-query.md](docs/crud-and-query.md) | 标签 / 资源 / 查询 API 落地说明 |
| [project.md](project.md) | 项目看板（当前状态与下一步） |
| [plans/](plans/) | 设计规格与实现计划 |

## 技术栈

- **.NET 8**（C#）：Service / CLI / MCP / Client；Explorer / Tray 为 `net8.0-windows`
- **SQLite** + EF Core Migrations
- **ASP.NET Core Minimal API**，本机 Bearer 鉴权
- 查询缓存：默认 Memory，可配 Redis / Null
- 浏览器扩展：MV3（Vite）

## 状态

v1 核心链路（Service、CRUD/查询、CLI、MCP、浏览器扩展、Explorer、Tray Find）已落地；安装器实现计划尚在路上。进度以 [project.md](project.md) 为准。

## 许可

尚未选定开源许可证；使用或分发前请先与仓库维护者确认。
