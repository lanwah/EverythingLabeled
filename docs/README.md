# EverythingLabeled 文档

本目录放**实现说明与开发指引**（如何运行、目录约定、已落地行为）。设计规格与契约以 [`plans/`](../plans/) 为准；看板活状态见 [`project.md`](../project.md)。

| 文档 | 对应实现 | 说明 |
|---|---|---|
| [Service 骨架](./service-skeleton.md) | backlog **#8** | 守护进程工程结构、启动、配置、health/鉴权、测试 |
| [CRUD 与查询](./crud-and-query.md) | backlog **#9** | 标签/资源/关系/查询落地布局、写路径 bump、缓存桩、如何跑测试 |
| [查询缓存](./query-cache.md) | backlog **#10** | Memory/Redis/Null、`Cache:*`、opt-in Redis 测 |
| [CLI](./cli.md) | backlog **#11** | `el` 工程布局、运行、配置优先级、如何跑 PathUri/Client/Cli 测试 |
