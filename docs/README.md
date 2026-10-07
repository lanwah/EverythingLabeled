# EverythingLabeled 文档

本目录放**实现说明与开发指引**（如何运行、目录约定、已落地行为）。设计规格与契约以 [`plans/`](../plans/) 为准；看板活状态见 [`project.md`](../project.md)。

## 编译

仓库根对 `EverythingLabeled.sln` 执行 `dotnet build`。需要 .NET 8 SDK。默认 Debug。

```bat
build.bat
build.bat -Configuration Release
```

```powershell
.\build.ps1
.\build.ps1 -Configuration Release
```

`build.bat` 调用同目录 `build.ps1`。等价命令：`dotnet build EverythingLabeled.sln`（可加 `-c Release`）。

| 文档 | 对应实现 | 说明 |
|---|---|---|
| [Service 骨架](./service-skeleton.md) | backlog **#8** | 守护进程工程结构、启动、配置、health/鉴权、测试 |
| [CRUD 与查询](./crud-and-query.md) | backlog **#9** | 标签/资源/关系/查询落地布局、写路径 bump、缓存桩、如何跑测试 |
| [查询缓存](./query-cache.md) | backlog **#10** | Memory/Redis/Null、`Cache:*`、opt-in Redis 测 |
| [CLI](./cli.md) | backlog **#11** | `el` 工程布局、运行、配置优先级、如何跑 PathUri/Client/Cli 测试 |
| [MCP](./mcp.md) | backlog **#12** | stdio host、`--help`/`--version`、配置优先级、Cursor `mcp.json`、Tools 测试 |
| [浏览器扩展](./browser-extension.md) | backlog **#13** | `ext/` 构建与加载、Options 手工配置、Service 依赖、手工联调清单 |
| [Explorer 上下文菜单](./explorer.md) | backlog **#14** | 稀疏包注册/卸载、Host 四 mode 自测、AppData 依赖、手工验收清单 |
