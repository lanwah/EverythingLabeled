# MCP stdio host（#12）实现计划讨论确认记录

日期：2026-10-06  
方式：主线澄清（brainstorm，带推荐答案 → 用户确认）Round 1–2  
已落盘同步：

- [MCP 规格](../2026-09-27-mcp.md)（产品规格不改；[规格审核](./2026-09-27-mcp-review.md)）
- [MCP 实现计划](../2026-10-06-mcp-impl.md)
- [路线图 backlog](../2026-09-23-roadmap-backlog.md)（#12 已确认实现计划）
- [项目看板](../../project.md)

前提（未作为开放题，与规格/架构/#11 一致）：v1 仅 stdio + Tools；无 Resources/Prompts、无 HTTP MCP / `MapMcp` / InProc、无标签/关系 CRUD tools、无 `el_meta`、不在 MCP 内启停服务或写 config、不装到 LocalAppData、不跟 `dotnet new mcpserver`（该模板要 .NET 10）、路径/URI/`kind` 只走 PathUri + Client、确认策略不进 Client（CLI 仍 TTY，MCP 用 `confirm`）。

---

## A. Round 1 — 交付、SDK、Client 缺口

### Q1 — #12 交付边界

**问题**：本轮做到哪一层？是否顺带 HTTP MCP / 安装？

| 选项 | 含义 |
|---|---|
| **A** | `EverythingLabeled.Mcp.Tools`（七个 `el_*` + 本地闸门/`untag` confirm）+ `EverythingLabeled.Mcp`（stdio host、`--config`/`--base-url`/`--token`、`--help`/`--version`）+ 测试 + `docs/mcp.md`。Client 只补 MCP 必需缺口（见 Q4）。不装到 LocalAppData，不做 HTTP MCP / Resources / 标签 CRUD tools |
| B | 只做 Tools 类库，host 下轮再接 |
| C | A + 本轮顺带 `MapMcp` / InProc |

**确认**：选推荐 **A**。

### Q2 — 官方 SDK 版本

**问题**：`ModelContextProtocol` 钉哪一档？（本仓 TFM 为 net8。）

| 选项 | 含义 |
|---|---|
| **A** | 钉 **2.2.0**（2026-08 稳定版，TFM 含 net8）。Host 工程引用 `ModelContextProtocol`；Tools 尽量只引用 `ModelContextProtocol.Core`（属性不够则改引用主包）。接受传递依赖 `Microsoft.Extensions.*` **10.0.10**（仍跑在 net8） |
| B | 钉 1.0.0 |
| C | 不跟官方 SDK，手写 JSON-RPC stdio |

**确认**：选推荐 **A**。

### Q3 — host 形态与 stdout

**问题**：进程怎么起、日志往哪写、HttpClient 寿命？

| 选项 | 含义 |
|---|---|
| **A** | `Host.CreateApplicationBuilder` + `AddMcpServer().WithStdioServerTransport().WithTools<...>()`。启动前若参数是 `--help`/`--version`：离线打到 stdout 后 exit 0，**不**进 MCP。进入会话后日志只用 `ILogger` → **stderr**。不引入 Serilog。进程级 **一个** `HttpClient` + `HttpElApi`，随 host dispose |
| B | 手写 `McpServer` 主循环，不用 Generic Host |
| C | 与 Service 共用 Serilog 文件日志 |

**确认**：选推荐 **A**。

### Q4 — `el_health` 与现有 config 加载

**问题**：`ElConfigLoader.Load` 现在缺 token 就 `config_missing`，与规格「health 只要 baseUrl」冲突。

| 选项 | 含义 |
|---|---|
| **A** | Client 增加 **`LoadBaseUrl`**（或同等）：flag/env/file 解析 `baseUrl`（规范规则与现 `Load` 相同），**不要求 token**。业务 tool 仍走现有 `Load`（url+token）。`el_health` 只用 `LoadBaseUrl`；有 url 无 token 时仍可 `HealthAsync`（请求不带 Authorization）。不写回、未知键仍忽略 |
| B | 改现有 `Load`：无 token 也成功，token 允许空；业务 tool 自己查空 token |
| C | MCP 自己再解析一份 config，不动 Client |

**确认**：选推荐 **A**。

### Q5 — 闸门校验放哪

**问题**：aliases / limit / expand / target / `FileTarget` 已在 CLI `CommandSupport`；MCP 是第二处消费者。

| 选项 | 含义 |
|---|---|
| **A** | 抽到 **Client**（`ElRequestRules`），CLI 改为调用；Tools 同一套。确认策略（TTY vs `confirm`）**不**进 Client |
| B | Tools 内复制一份闸门，CLI 不动 |
| C | Tools 引用 Cli 工程 |

**确认**：选推荐 **A**。

### Q6 — 测试怎么切

**问题**：门禁测到哪一层？

| 选项 | 含义 |
|---|---|
| **A** | `Mcp.Tools.Tests`：假 `IElApi`，断言 JSON `{data,message}` / `{error}`、`isError`、untag confirm、aliases/expand/health。`Mcp.Tests`：`--help`/`--version` 离线、flag 覆盖注入。不做 MCP Inspector、不做真 stdio 联调作为门禁 |
| B | 再加官方 MCP client 对 stdio 的协议级测试作为门禁 |
| C | 只测 host 起得来，工具逻辑靠手工 |

**确认**：选推荐 **A**。B 可留 backlog。

### Q7 — 产物名、版本、文档

**问题**：exe 名、版本、是否写 docs？

| 选项 | 含义 |
|---|---|
| **A** | 工程/exe：`EverythingLabeled.Mcp`。`<Version>0.1.0</Version>`，`--version` 一行 `0.1.0`。`docs/mcp.md` + `docs/README.md` 链过去。开发期 framework-dependent，不装 LocalAppData |
| B | AssemblyName `el-mcp` |
| C | 本轮不写 docs，只代码 |

**确认**：选推荐 **A**。

### Q8 — `confirmation_required` 码的 SSOT

**问题**：该本地码放哪？

| 选项 | 含义 |
|---|---|
| **A** | 加到 Client `ElErrorCodes.ConfirmationRequired = "confirmation_required"`；Tools 用它组 `{error}`。Client **不**实现确认流程 |
| B | 只在 Tools 里写字符串常量 |
| C | 放进 `IElApi.UntagAsync` 参数 |

**确认**：选推荐 **A**。

---

## B. Round 2 — host 接线、schema、日志

### Q9 — flag 与 Generic Host 抢 args

**问题**：`Host.CreateApplicationBuilder(args)` 会把 `--config` 等当成 host 配置。

| 选项 | 含义 |
|---|---|
| **A** | 先用 **SCL 2.0.12** 解析 `--help` / `--version` / `--config` / `--base-url` / `--token`（未知参数启动前失败、不进会话）。`--help`/`--version` 离线 stdout 后退出。其余情况 `Host.CreateApplicationBuilder` **传空 args**，再把解析结果注入 DI |
| B | 手写解析这几个 flag，host 同样空 args |
| C | 把进程 args 原样交给 Generic Host |

**确认**：选推荐 **A**。

### Q10 — config 生命周期

**问题**：stdio 进程可能挂很久，是否热读 `config.json`？

| 选项 | 含义 |
|---|---|
| **A** | **启动时快照一次**：flag/env/文件 → 配置，一个 `HttpClient`（Timeout **30s**）+ `HttpElApi`。改 `config.json` 要重启 MCP 进程。不按 tool 调用重读 |
| B | 每次 tool 调用重读文件（flag/env 仍以启动为准） |
| C | `FileSystemWatcher` 热更新 |

**确认**：选推荐 **A**。

### Q11 — 缺配置时还进不进 MCP 会话

**问题**：无 url/token 时进程是否仍被 Cursor 拉起？

| 选项 | 含义 |
|---|---|
| **A** | 只要不是 `--help`/`--version`，**始终进 stdio**。无 `baseUrl` → 不建 `HttpElApi`；`el_health` 与业务 tool 都返回 `config_missing`。有 `baseUrl` 无 token → 仍建 `HttpElApi`（token 空字符串即可，health 不带 Authorization）；业务 tool **调用前**再查 token，缺则 `config_missing`，不发请求 |
| B | 缺 url+token 则进程 exit 非 0，不进会话 |
| C | 缺配置时进会话但 `tools/list` 为空 |

**确认**：选推荐 **A**。

### Q12 — 抽到 Client 的闸门文案

**问题**：CLI 现有 flag 口吻（`--expand`）与 MCP 参数名不一致。

| 选项 | 含义 |
|---|---|
| **A** | Client `ElRequestRules` 英文消息用 **规格参数名**（`expand`、`max_depth`、`limit`…）。CLI 直接用同一句。测试继续以 **code** 为主，不钉死旧 CLI 句 |
| B | Client 只出 code；CLI / MCP 各写一套 message |
| C | Client 保持 CLI flag 文案；MCP 再改写成参数名 |

**确认**：选推荐 **A**。

### Q13 — tool JSON schema 参数名

**问题**：对外参数用 snake_case 还是 camelCase？

| 选项 | 含义 |
|---|---|
| **A** | 协议参数 **snake_case**，与规格表一致。方法名 PascalCase 无妨，**对外 Name 必须 `el_*`** |
| B | schema 用 camelCase（`createMissing`） |
| C | 两种名字都接受 |

**确认**：选推荐 **A**。

### Q14 — stderr 日志量

**问题**：SDK 示例常用 Trace，会吵。

| 选项 | 含义 |
|---|---|
| **A** | Console → stderr；默认 **Warning** 及以上。不写文件、不用 Serilog |
| B | Information |
| C | Trace |

**确认**：选推荐 **A**。

### Q15 — SDK 工具注解

**问题**：除 Description 外是否设置 `Destructive` / `ReadOnly`？

| 选项 | 含义 |
|---|---|
| **A** | `el_untag` `Destructive=true`；`el_tags` / `el_query` / `el_preview` / `el_suggest` / `el_health` `ReadOnly=true`；`el_tag` 不标 ReadOnly。Description 仍写确认表。不启用 Resources/Prompts |
| B | 只写 Description |
| C | 全部标 OpenWorld |

**确认**：选推荐 **A**。
