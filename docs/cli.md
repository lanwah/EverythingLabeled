# CLI（#11）

薄客户端 `el`（`src/EverythingLabeled.Cli`，AssemblyName `el`）。只走 Service HTTP + Bearer；URI 规范化在 `EverythingLabeled.PathUri`，HTTP/DTO 在 `EverythingLabeled.Client`。命令面、退出码与边界以 [CLI 规格](../plans/2026-09-26-cli.md) 为准（审核：[CLI 规格审核](../plans/confirmed/2026-09-26-cli-review.md)）。

## 前置

1. **EverythingLabeled.Service** 已在跑。
2. `%AppData%\EverythingLabeled\config.json` 含必填 `baseUrl`、`token`（首次由 Service 引导写入）。键表见 [本机配置](./config.md)。
3. 本机可执行 `el`（`dotnet run`、或把输出目录加入 PATH）。

## 运行

```text
dotnet run --project src/EverythingLabeled.Cli -- --help
el --help
el --version
```

`--help` / `--version` **不读 config、不连服务**，可离线。

配置**不写回**。覆盖顺序：`--config` / `--base-url` / `--token` > env `EL_CONFIG` / `EL_BASE_URL` / `EL_TOKEN` > `%APPDATA%\EverythingLabeled\config.json`。

全局选项（各子命令可用）：`--json`、`--base-url`、`--token`、`--config`、`-h` / `--help`。`--config` **无**短选项（避免与 `-c` = `--create` 冲突）。

## 命令一览

| 命令 | 作用 | 示例 |
|---|---|---|
| `tag` | 给资源打标 | `el tag D:\Notes\a.md 工作 重要` |
| `untag` | 去掉资源上的标签绑定 | `el untag .\a.md 工作 -y` |
| `tags` | 列出资源上的别名 | `el tags .\a.md` |
| `query` | 按标签表达式查资源 | `el query "工作 重要"` |
| `preview` | 查询预览（不分页总数） | `el preview 工作` |
| `suggest` | 标签别名联想 | `el suggest 工` |
| `completion` | 打印壳补全脚本 | `el completion powershell` |

v1 **不做**：标签/关系 CRUD、启停服务、写 config、目录递归批量打标、一次多个 target、非 PowerShell 补全。

### `el tag <target> <alias>...`

- 至少 1 个、最多 100 个别名。
- 默认**不**自动建标；加 `-c` / `--create` 才 `createMissing`。
- 本机路径 / `file://`：**必须存在**（文件或目录）；`http(s)` 不查本地。
- 仅 `tag` 推断并提交 `kind`：`http(s)` → `url`；存在目录 → `folder`；其余 file → `file`。

```text
el tag .\a.md 工作
el tag .\a.md 新标签 -c
el tag "https://example.com/doc" 资料
```

### `el untag <target> <alias>...`

- 去掉的是资源上的**绑定**，不是删除 Tag 实体；永不自动建标。
- 本机路径**不**做存在性检查（可对已删文件对应 URI 操作）。
- 确认：config `confirmUntag` 缺省 `true`；`-y` / `--yes` 跳过本次。非 TTY（管道/CI）且需确认时必须带 `--yes`，否则本地失败、不发请求。

```text
el untag .\a.md 工作
el untag .\a.md 工作 -y
```

### `el tags <target>`

列出该资源上的别名；不检查本机路径是否存在。

```text
el tags .\a.md
el tags .\a.md --json
```

### `el query <q>` / `el preview <q>`

- `<q>` **恰好一个**位置参数；含空格由壳引用。CLI **不**改写分词，原样作为 `q=`。
- 以 `-` 开头的负词须加 `--`：`el query -- -archived`。
- 查询语法见 [查询语言细则](../plans/2026-09-26-query-language.md)。

| 选项 | 含义 | 默认 |
|---|---|---|
| （无 `--expand`） | 不展开 | — |
| `--expand child` | 展开子标签（v1 仅 `child`） | — |
| `--max-depth` | 展开深度 | 仅 expand 时；省略用服务端 **8** |
| `--expanded-tag-limit` | 展开标签上限 | 仅 expand 时；省略用服务端 **512** |
| `--limit` | 页大小 | `query` **50**；`preview` **10**；上限 **1024** |
| `--offset` | 偏移 | **0**（仅 `query`） |

未带 `--expand` 却写 `--max-depth` / `--expanded-tag-limit` → 本地报错。

```text
el query "工作 重要" --limit 20
el query -- -归档
el preview 工作 --expand child
```

### `el suggest <prefix>`

```text
el suggest 工
el suggest 工 --limit 8
```

`--limit` 缺省 **10**（上限同 API）。

### `el completion powershell`

将 PowerShell 补全脚本打到 **stdout**（打印脚本本身可离线）。注册示例：

```powershell
el completion powershell | Out-String | Invoke-Expression
```

v1 仅 PowerShell。补全时调 `suggest`；服务不可达则静默空补全。

## Target 与输出

**Target**：本机路径（相对 → 当前目录）、`file://`、`http(s)://`、UNC。一次一个 target；多文件用壳循环。

**输出**：

- 默认人类可读；`query` / `preview` 每行一个资源，分页摘要与 API `message`（notice）在 **stderr**。
- `--json`：成功时 stdout **仅** API `data`；错误时 stderr 为 `{ "error": { "code", "message" } }`。

**退出码（摘要）**：`0` 成功；`1` 本地用法/校验（含 untag 取消）；`2` 配置缺失或连不上服务；`3` HTTP 4xx；`4` HTTP 5xx/其它网络失败。完整表见 [规格 §6](../plans/2026-09-26-cli.md)。

## 工程布局

- `src/EverythingLabeled.PathUri` — 路径/`file://`/`http(s)` 规范化
- `src/EverythingLabeled.Client` — `ElConfig`、`ElJson`、`IElApi`/`HttpElApi`、`FileTarget`
- `src/EverythingLabeled.Cli` — SCL 解析、人类/`--json` 输出、untag 确认、`completion powershell`；exe 图标 `Assets/cli.ico`（命令提示符 E756 + 标签角标；`scripts/make-cli-icon.ps1`）
- 对应测试：`tests/EverythingLabeled.PathUri.Tests`、`Client.Tests`、`Cli.Tests`

## 测试

```text
dotnet test tests/EverythingLabeled.PathUri.Tests
dotnet test tests/EverythingLabeled.Client.Tests
dotnet test tests/EverythingLabeled.Cli.Tests
```

或 `dotnet test EverythingLabeled.sln`。
