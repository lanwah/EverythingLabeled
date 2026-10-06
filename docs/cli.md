# CLI（#11）

薄客户端 `el`（`src/EverythingLabeled.Cli`，AssemblyName `el`）。只走 Service HTTP + Bearer；URI 规范化在 `EverythingLabeled.PathUri`，HTTP/DTO 在 `EverythingLabeled.Client`。命令面与退出码以 [CLI 规格](../plans/2026-09-26-cli.md) 为准。

## 工程布局

- `src/EverythingLabeled.PathUri` — 路径/`file://`/`http(s)` 规范化
- `src/EverythingLabeled.Client` — `ElConfig`、`ElJson`、`IElApi`/`HttpElApi`、`FileTarget`
- `src/EverythingLabeled.Cli` — SCL 解析、人类/`--json` 输出、untag 确认、`completion powershell`
- 对应测试：`tests/EverythingLabeled.PathUri.Tests`、`Client.Tests`、`Cli.Tests`

## 运行

```text
dotnet run --project src/EverythingLabeled.Cli -- --help
```

配置不写回。覆盖顺序：`--config` / `--base-url` / `--token` > env `EL_CONFIG` / `EL_BASE_URL` / `EL_TOKEN` > `%APPDATA%\EverythingLabeled\config.json`。`baseUrl` 与 `token` 必填。

## 测试

```text
dotnet test tests/EverythingLabeled.PathUri.Tests
dotnet test tests/EverythingLabeled.Client.Tests
dotnet test tests/EverythingLabeled.Cli.Tests
```

或 `dotnet test EverythingLabeled.sln`。
