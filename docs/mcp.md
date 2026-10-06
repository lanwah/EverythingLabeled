# MCP stdio host（#12）

薄客户端 `EverythingLabeled.Mcp`（`src/EverythingLabeled.Mcp`）。stdio MCP 会话；七个 `el_*` 工具在 `EverythingLabeled.Mcp.Tools`，只走 Service HTTP + Bearer。闸门与配置走 [Client](../src/EverythingLabeled.Client/)（与 [CLI](./cli.md) 共用 `ElRequestRules` / `ElConfigLoader`）。成功/失败信封按规格 §6：`{"data":...,"message":null|string}` / `{"error":{"code","message"}}`，**不是** CLI `--json` 的裸 `data`。规格以 [MCP](../plans/2026-09-27-mcp.md) 与 [实现计划](../plans/2026-10-06-mcp-impl.md) 为准。

## 工程布局

- `src/EverythingLabeled.Mcp.Tools` — `ElTools`、`ElMcpRuntime`、`ElMcpJson`（ModelContextProtocol.Core 2.2.0）
- `src/EverythingLabeled.Mcp` — SCL 解析 `--config` / `--base-url` / `--token`，Generic Host + stdio transport
- `src/EverythingLabeled.Client` — `ElConfigLoader`、`HttpElApi`（host 不重复解析 JSON）
- 对应测试：`tests/EverythingLabeled.Mcp.Tools.Tests`、`tests/EverythingLabeled.Mcp.Tests`

**不要**引用 `ModelContextProtocol.AspNetCore` 或 `EverythingLabeled.Service`。stdout 只走 MCP 协议帧；日志阈值 Warning 且写入 stderr。缺配置仍启动会话，由工具返回 `config_missing`。

## 运行

```text
dotnet run --project src/EverythingLabeled.Mcp -- --help
```

配置不写回。覆盖顺序：`--config` / `--base-url` / `--token` > env `EL_CONFIG` / `EL_BASE_URL` / `EL_TOKEN` > `%APPDATA%\EverythingLabeled\config.json`。`--help` / `--version` 不读配置、不进 host。

业务工具需要 `baseUrl` 与 `token`。`el_health` 只需 `baseUrl`；服务不可达时工具返回 `connection_failed`。两者都缺时会话仍启动；`el_health` 在缺 url 时、其余工具在缺 url 或 token 时返回 `config_missing`。

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

## 测试

```text
dotnet test tests/EverythingLabeled.Mcp.Tools.Tests
dotnet test tests/EverythingLabeled.Mcp.Tests
```

或 `dotnet test EverythingLabeled.sln`。
