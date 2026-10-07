# Explorer 上下文菜单（#14）

Windows 稀疏包 `IExplorerCommand` + 进程外 WPF `EverythingLabeled.ExplorerHost`。菜单 DLL 只写 `--paths-file` 并即发即忘启动 Host；业务与 HTTP 全在 Host。规格以 [Explorer 集成](../plans/2026-09-27-explorer-integration.md) 与 [实现计划](../plans/2026-10-07-explorer-impl.md) 为准。

## 工程布局

| 项目 | 说明 |
|---|---|
| `src/EverythingLabeled.ExplorerShared` | paths-file（tmp / Cleanup / Write / ReadAndDelete） |
| `src/EverythingLabeled.ExplorerHost` | WPF Host：`--mode tag\|untag\|tags\|find`，可选 `--paths-file` |
| `src/EverythingLabeled.ExplorerCommand` | COM 菜单 DLL（`EnableComHosting` → `*.comhost.dll`）+ `Package/AppxManifest.xml` |

Explorer 相关工程须 **Windows x64**（`net8.0-windows`）。

## 构建与测试

```powershell
dotnet build src/EverythingLabeled.ExplorerCommand/EverythingLabeled.ExplorerCommand.csproj
dotnet test tests/EverythingLabeled.ExplorerHost.Tests
```

Command 的 csproj 会先编 Host，并把 Host **整输出目录**拷到 Command 输出旁（含 `EverythingLabeled.ExplorerHost.exe` 与依赖）。

## 命令行启动 Host（四 mode）

Service 已起、`%AppData%\EverythingLabeled\config.json` 已填时，可在不注册稀疏包的情况下自测 Host：

```powershell
# 按标签查找（无选中）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode find

# 看标签（单选；paths-file 一行一个绝对路径）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode tags --paths-file <paths.txt>

# 打标
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode tag --paths-file <paths.txt>

# 去标
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode untag --paths-file <paths.txt>
```

`find` 可省略 `--paths-file`；若传入，Host **忽略**其内容。

## 开发 Identity（钉死）

| 字段 | 值 |
|---|---|
| Name | `EverythingLabeled.ExplorerDev` |
| Publisher | `CN=EverythingLabeled-Dev` |
| DisplayName / PublisherDisplayName | `EverythingLabeled` |

**不**把私钥 / `.pfx` 提交进 git。本机自签或测试证书仅留在开发机。

## 侧载前提

1. **设置 → 系统 → 开发者选项**（或「面向开发人员」）→ 打开 **开发人员模式**。
2. 准备自签代码签名证书，Subject **必须**为 `CN=EverythingLabeled-Dev`（与 Manifest `Publisher` 一致），并导入本机「受信任的根证书颁发机构」或「受信任的人」（按本机策略）。示例（仅本机，勿提交私钥）：

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=EverythingLabeled-Dev" `
  -KeyUsage DigitalSignature -FriendlyName "EverythingLabeled Explorer Dev" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
Export-Certificate -Cert $cert -FilePath "$env:TEMP\el-dev.cer"
# 需管理员或 UI 确认：导入到 Trusted Root / Trusted People
Import-Certificate -FilePath "$env:TEMP\el-dev.cer" -CertStoreLocation Cert:\LocalMachine\Root
```

## 注册（稀疏包 + ExternalLocation）

输出目录（示例）：

`src\EverythingLabeled.ExplorerCommand\bin\Debug\net8.0-windows\`

其中应有：`EverythingLabeled.ExplorerCommand.comhost.dll`、`AppxManifest.xml`、`Assets\`、以及拷贝来的 `EverythingLabeled.ExplorerHost.exe` 与依赖。

`uap10:AllowExternalContent` 包必须用 **ExternalLocation** 指向该输出目录；不能只对 Manifest 做无外部位置的 `-Register`。

```powershell
$out = (Resolve-Path "src\EverythingLabeled.ExplorerCommand\bin\Debug\net8.0-windows").Path
$stage = Join-Path $env:TEMP "el-explorer-sparse"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Join-Path $stage "Assets") | Out-Null
Copy-Item (Join-Path $out "AppxManifest.xml") $stage
Copy-Item (Join-Path $out "Assets\*") (Join-Path $stage "Assets")
$msix = Join-Path $env:TEMP "EverythingLabeled.ExplorerDev.msix"
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\<ver>\x64\makeappx.exe" pack /d $stage /p $msix /nv
& "${env:ProgramFiles(x86)}\Windows Kits\10\bin\<ver>\x64\signtool.exe" sign /fd SHA256 /n "EverythingLabeled-Dev" $msix
Add-AppxPackage -Path $msix -ExternalLocation $out
```

注册后：资源管理器右键文件/文件夹应见应用名 **EverythingLabeled** 下的「打标 / 去标 / 看标签 / 按标签查找」；文件夹空白处仅「按标签查找」。菜单 Invoke **不**等待 Host 退出。

若菜单未出现：结束并重启 `explorer.exe`，或注销再登录。

## 卸载

```powershell
Get-AppxPackage -Name EverythingLabeled.ExplorerDev | Remove-AppxPackage
```

包全名以 `Get-AppxPackage` 输出为准（含版本与 Publisher 哈希后缀）。

## 依赖

- **Service**：本机已监听 `127.0.0.1`，Host 经 Bearer 调 `/v1`（health 失败则 UI 提示，业务不可用）。
- **`%AppData%\EverythingLabeled\config.json`**：Host **只读**、**不写回**；忽略 `EL_*` 环境变量。与 CLI 共用同一文件，至少需 `baseUrl` 与 `token`。Explorer 相关可选字段：
  - `previewLimit` — find 预览条数，缺省 **16**（有效范围 1–50）
  - `maxConcurrency` — 批量打标/去标并发，缺省 **4**
  - `confirmUntag` — 去标前确认，缺省 **true**

## 手工验收清单

稀疏包注册完成、Service 与 config 就绪后，建议对齐 [规格 §10](../plans/2026-09-27-explorer-integration.md#10-验收清单规格层) 逐项确认：

- [ ] 稀疏包：子菜单四项（中文项名）；空白处仅「按标签查找」；看标签仅单选；Invoke 即发即忘
- [ ] Host 硬规则：非法丢弃并提示 N；URI 去重；>100 拒绝不截断；DLL 不因数量灰打标/去标
- [ ] PathUri 与 CLI **同一库**；含 UNC / symlink / junction；打标预检排除无效项
- [ ] 启动必做 health（+ meta）；`baseUrl` 仅 127.0.0.1
- [ ] 打标/去标每次一别名；批量并发=`maxConcurrency`（缺省 4）；可取消；401 中止、其它按项继续
- [ ] find：仅 preview；打开后窗口保持；无 expand
- [ ] paths-file 私有 tmp + 读完删除；只读 AppData config（含 `previewLimit` / `maxConcurrency`）
- [ ] Command DLL 不承载业务 HTTP；Host 崩溃不拖死 Explorer（预期）
