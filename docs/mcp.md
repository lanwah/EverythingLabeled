# MCP stdio host（#12）

薄客户端 `EverythingLabeled.Mcp`（`src/EverythingLabeled.Mcp`）。stdio MCP 会话；七个 `el_*` 工具在 `EverythingLabeled.Mcp.Tools`，只走 Service HTTP + Bearer。闸门与配置走 [Client](../src/EverythingLabeled.Client/)（与 [CLI](./cli.md) 共用 `ElRequestRules` / `ElConfigLoader`）。工具面、参数与边界以 [MCP 规格](../plans/2026-09-27-mcp.md) 为准（审核：[MCP 规格审核](../plans/confirmed/2026-09-27-mcp-review.md)）。

## 前置

1. **EverythingLabeled.Service** 已在跑。
2. `%AppData%\EverythingLabeled\config.json` 含必填 `baseUrl`、`token`（首次由 Service 引导写入）。键表见 [本机配置](./config.md)。
3. 宿主（Cursor / Claude Desktop 等）能启动 `EverythingLabeled.Mcp.exe`，或开发期 `dotnet run --project src/EverythingLabeled.Mcp`。

v1 仅 **stdio + Tools**；无 Resources / Prompts。MCP **不**拉起 Service、不写 config。

## 运行

```text
dotnet run --project src/EverythingLabeled.Mcp -- --help
EverythingLabeled.Mcp.exe --help
EverythingLabeled.Mcp.exe --version
```

`--help` / `--version` **不读 config、不连服务**，可离线。

Host 选项：`--config` / `--base-url` / `--token`。配置**不写回**。覆盖顺序：`--config` / `--base-url` / `--token` > env `EL_CONFIG` / `EL_BASE_URL` / `EL_TOKEN` > `%APPDATA%\EverythingLabeled\config.json`。

缺配置时 **仍进入 MCP 会话**；业务 tool 返回 `config_missing`，`el_health` 在缺 `baseUrl` 时亦然。服务不可达 → `connection_failed`（整请求超时默认 **30s**）。

**不要**引用 `ModelContextProtocol.AspNetCore` 或 `EverythingLabeled.Service`。stdout 只走 MCP 协议帧；日志阈值 Warning 且写入 stderr。

## 工具一览

| Tool | 作用 | 对应 CLI / HTTP |
|---|---|---|
| `el_tag` | 给资源打标 | `el tag` → `POST /v1/resources/tags` add |
| `el_untag` | 去掉资源上的标签绑定（destructive） | `el untag` → remove |
| `el_tags` | 列出资源上的别名 | `el tags` → `GET /v1/resources/tags` |
| `el_query` | 按标签表达式查资源（可分页） | `el query` → `GET /v1/query` |
| `el_preview` | 查询预览（无分页总数） | `el preview` → `GET /v1/query/preview` |
| `el_suggest` | 标签别名联想 | `el suggest` → `GET /v1/tags/suggest` |
| `el_health` | 探活（**不需要 token**） | `GET /v1/health` |

v1 **不做**：标签/关系 CRUD、`el_meta`、`completion`、启停 Service、写 config、多 target、目录递归批量打标、打开/启动资源、HTTP MCP。

### `el_tag`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `target` | string | — | 本机路径或 URI |
| `aliases` | string[] | — | 1–100 个别名；**仅数组**，不接受逗号拼接单字符串 |
| `create_missing` | bool | `false` | 为 `true` 时 API `createMissing: true` |

- 本机路径 / `file://`：**必须存在**（含 symlink 目标；断链 → `path_not_found`）；`http(s)` 不查本地。
- 仅 `el_tag` 推断并提交 `kind`：`http(s)` → `url`；存在目录 → `folder`；其余 file → `file`。
- 相对路径相对 **MCP 进程 cwd**（可在 `mcp.json` 设 `cwd`）；不确定时用绝对路径或 URI。

```json
{ "target": "D:\\Notes\\a.md", "aliases": ["工作", "重要"] }
{ "target": "https://example.com/doc", "aliases": ["资料"], "create_missing": true }
```

### `el_untag`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `target` | string | — | 本机路径或 URI |
| `aliases` | string[] | — | 1–100 个别名 |
| `confirm` | bool | `false` | `confirmUntag` 为 true（缺省）时须 `true` 才执行 |

- Tool 标记 **destructive**；去掉的是资源上的**绑定**，不是删除 Tag 实体；永不 `createMissing`。
- 本机路径**不**做存在性检查。
- 确认（无 TTY，等价于 CLI `-y` / `--yes`）：

| 条件 | 行为 |
|---|---|
| config `confirmUntag == false` | 直接 remove |
| `confirmUntag` 缺省 **true** 且 `confirm != true` | **不发请求**；`confirmation_required` |
| 同上且 `confirm == true` | 直接 remove |

```json
{ "target": ".\\a.md", "aliases": ["工作"], "confirm": true }
```

### `el_tags`

| 参数 | 类型 | 说明 |
|---|---|---|
| `target` | string | 本机路径或 URI |

列出该资源上的别名；不检查本机路径是否存在。

```json
{ "target": ".\\a.md" }
```

### `el_query` / `el_preview`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `q` | string | — | 查询表达式；原样提交，trim 后空 → `bad_request` |
| `expand` | string? | — | 仅 `"child"`；v1 仅此一种 |
| `max_depth` | int? | — | 展开深度；**须**与 `expand` 同传；省略用服务端 **8** |
| `expanded_tag_limit` | int? | — | 展开标签上限；**须**与 `expand` 同传；省略用服务端 **512** |
| `limit` | int? | `query` **50** / `preview` **10** | 页大小；上限 **1024** |
| `offset` | int? | **0** | 仅 `el_query` |

- 大结果集建议 Agent **先 `el_preview`**，再按需 `el_query` 分页。
- 未传 `expand` 却传 `max_depth` / `expanded_tag_limit` → 本地 `bad_request`。
- 查询语法见 [查询语言细则](../plans/2026-09-26-query-language.md)。

```json
{ "q": "工作 重要", "limit": 20 }
{ "q": "-archived" }
{ "q": "工作", "expand": "child", "limit": 10 }
```

### `el_suggest`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `prefix` | string | — | 别名前缀；trim 后空 → `bad_request` |
| `limit` | int? | **10** | 条数上限同 API |

```json
{ "prefix": "工", "limit": 8 }
```

### `el_health`

无参数。

- 调用 `GET /v1/health`（API **免鉴权**）。
- **只需** `baseUrl`；缺则 `config_missing`；config 中的 `token` 可忽略。
- 用于探活；验 token 靠其它业务 tool 的 `unauthorized`。

## Target 与返回

**Target**：本机路径（相对 → MCP 进程 cwd）、`file://`、`http(s)://`、UNC。一次一个 target；多文件由 Agent 多次调用。

**成功**（**有意不同于** CLI `--json` 的裸 `data`；Agent 无可靠 stderr notice 通道）：

```json
{
  "data": { },
  "message": null
}
```

- `data`：API 信封中的业务 `data`。
- `message`：API 信封的 `message`；无 notice 时为 **`null`**；非空时**不得丢弃**。
- 工具结果 `isError: false`（含带 notice 的软成功）。

**失败**：

```json
{
  "error": {
    "code": "bad_request",
    "message": "…"
  }
}
```

- 工具结果 `isError: true`。

本地 `code`（摘要）：

| code | 场景 |
|---|---|
| `config_missing` | 缺 `baseUrl` 或（业务 tool）缺 `token` |
| `path_not_found` | `el_tag` 目标路径 / `file://` 本地不存在或断链 |
| `confirmation_required` | `el_untag` 需要 `confirm=true` 而未提供 |
| `bad_request` | 其它用法/校验错误 |
| `connection_failed` | 无法连接 `baseUrl` 或请求超时 |

HTTP 业务错误透传 API `code` + `message`（如 `tag_not_found`、`unauthorized`）。stdio 对 Agent **无** CLI 式 exit code 语义。完整表见 [规格 §6](../plans/2026-09-27-mcp.md)。

## Cursor `mcp.json` 示例

`command` 指向编译出的 exe；可选 `cwd` / `env`。

```json
{
  "mcpServers": {
    "everything-labeled": {
      "command": "D:/path/to/EverythingLabeled.Mcp.exe",
      "cwd": "D:/path/to/working-dir",
      "env": {
        "EL_BASE_URL": "http://127.0.0.1:17321/",
        "EL_TOKEN": "your-token"
      }
    }
  }
}
```

- 通常只配 `command`；token 走 AppData。
- `cwd` **可选**：影响相对路径解析。
- 需要覆盖时用 `env`（`EL_BASE_URL` / `EL_TOKEN` / `EL_CONFIG`）。
- 服务未启动 → 业务 tool 返回 `connection_failed`；可用 `el_health` 探活。

## 工程布局

- `src/EverythingLabeled.Mcp.Tools` — `ElTools`、`ElMcpRuntime`、`ElMcpJson`（ModelContextProtocol.Core 2.2.0）
- `src/EverythingLabeled.Mcp` — SCL 解析、`Generic Host` + stdio transport；exe 图标 `Assets/mcp.ico`（拼图/插件 E895 + 标签角标；`scripts/make-mcp-icon.ps1`）
- `src/EverythingLabeled.Client` — `ElConfigLoader`、`HttpElApi`（host 不重复解析 JSON）
- 对应测试：`tests/EverythingLabeled.Mcp.Tools.Tests`、`tests/EverythingLabeled.Mcp.Tests`

## 测试

```text
dotnet test tests/EverythingLabeled.Mcp.Tools.Tests
dotnet test tests/EverythingLabeled.Mcp.Tests
```

或 `dotnet test EverythingLabeled.sln`。
