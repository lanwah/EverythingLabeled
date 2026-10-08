# Explorer 上下文菜单（#14）

Windows 稀疏包 `IExplorerCommand` + 进程外 WPF `EverythingLabeled.ExplorerHost`。菜单 DLL 只写 `--paths-file` 并即发即忘启动 Host；业务与 HTTP 全在 Host。规格以 [Explorer 集成](../plans/2026-09-27-explorer-integration.md) 与 [实现计划](../plans/2026-10-07-explorer-impl.md) 为准。

## 工程布局

| 项目 | 说明 |
|---|---|
| `src/EverythingLabeled.ExplorerShared` | paths-file（tmp / Cleanup / Write / ReadAndDelete） |
| `src/EverythingLabeled.ExplorerHost` | WPF Host：`--mode tag\|untag\|tags\|find`，可选 `--paths-file` |
| `src/EverythingLabeled.ExplorerCommand` | COM 菜单 DLL（`EnableComHosting` → `*.comhost.dll`）+ `Package/AppxManifest.xml` |
| `src/EverythingLabeled.Tray` | WinForms 托盘：全局热键打开 Find；构建时拷 Host 整目录到 Tray 旁 → [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md) · [实现计划](../plans/2026-10-07-tray-find-hotkey-impl.md) |

Explorer 相关工程须 **Windows x64**（`net8.0-windows`）。

### 四者关系与依赖

```text
                    ┌──────────────────────────┐
                    │   ExplorerShared         │  paths-file + Find 单实例 IPC
                    │   （无对外 ProjectRef）   │
                    └────────────▲─────────────┘
           ProjectReference      │      ProjectReference
        ┌────────────────────────┼────────────────────────┐
        │                        │                        │
┌───────┴────────┐      ┌────────┴────────┐      ┌────────┴────────┐
│ ExplorerHost   │      │ ExplorerCommand │      │ Tray            │
│ WPF 业务 UI    │◄─拷──│ 稀疏包 COM 菜单 │      │ 托盘 + 热键     │
│ + Client HTTP  │      │ 即发即忘起 Host │      │ 唤起/前置 Find  │
└───────▲────────┘      └─────────────────┘      └────────┬────────┘
        │ 编后整目录拷贝到输出旁（含 .exe 与依赖）          │
        └──────────────────────────────────────────────────┘
```

| 方向 | 含义 |
|---|---|
| **Shared ← Host / Command / Tray** | 三者都 `ProjectReference` Shared。Shared **不**引用另外三者；只提供 paths-file 约定与 `FindHostIpc`（Mutex + 命名管道 `Activate\n`）。 |
| **Host ← Command / Tray（构建）** | Command、Tray 的 csproj **引用 Host**，以便先编 Host，再把 Host **整输出目录**拷到各自 `bin\...\`（同目录须有 `EverythingLabeled.ExplorerHost.exe`）。这是部署耦合，不是业务库引用。 |
| **Host → Client / Shared** | Host 唯一承载业务与 HTTP：依赖 `EverythingLabeled.Client`（Bearer → Service）与 Shared；**不**依赖 Command / Tray。 |
| **Command → Shared**（运行时） | 菜单 DLL 写 `--paths-file` 后即发即忘启动**同目录** Host；不做 HTTP、不读 token、不等待 Host 退出。 |
| **Tray → Shared**（运行时） | 热键/菜单经 `FindHostIpc` 激活已有 Find，失败则启动同目录 Host `--mode find`；**不**调业务 HTTP。退出 Tray **不**杀已开 Host。 |
| **Command ⟂ Tray** | 无编译依赖。Find 单实例靠 Shared 的 IPC 约定对齐（右键 find / 热键 / 托盘 / CLI 同源）。 |

要点：

- **业务中心是 Host**：打标 / 去标 / 看标签 / Find UI 与 Service 通信都在 Host；Command 与 Tray 只是入口。
- **Shared 是胶水**：路径文件协议 + Find 单实例 IPC；改约定须三端一起跟。
- **两套输出旁各有一份 Host**：稀疏包 `ExternalLocation` 指向 Command 输出；Tray 日常自启用 Tray 输出。两边都靠构建拷贝，缺 exe 则菜单/热键起不来。

## 构建与测试

```powershell
dotnet build src/EverythingLabeled.ExplorerCommand/EverythingLabeled.ExplorerCommand.csproj
dotnet build src/EverythingLabeled.Tray/EverythingLabeled.Tray.csproj
dotnet test tests/EverythingLabeled.ExplorerHost.Tests
dotnet test tests/EverythingLabeled.Tray.Tests
```

Command / Tray 的 csproj 会先编 Host，并把 Host **整输出目录**拷到各自输出旁（含 `EverythingLabeled.ExplorerHost.exe` 与依赖）。

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
  - `findHotkey` / `runAtStartup` — Tray 用（Host 可不解析）；见 [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md)

### Tray（Find 热键）

- 工程：`src/EverythingLabeled.Tray`。输出目录须有同目录 `EverythingLabeled.ExplorerHost.exe`（构建已自动拷贝）。
- 默认热键 `Alt+L`（config `findHotkey`；空串/`none` 关热键；改 JSON 后重启 Tray）；菜单仍可「打开查找」。
- HKCU Run 值名 `EverythingLabeled.Tray`（与 Service 的 `EverythingLabeled` 并存）；`runAtStartup` 默认 true，托盘可勾选切换。
- 首次需手动启动一次 Tray（本切片无安装器自动拉起）。
- Find **全入口单实例**（热键 / 托盘 / 右键 / CLI）：Mutex + 管道 `Activate\n`；关窗后下次热键新开 Host；退出 Tray **不**杀已开 Host。

```powershell
dotnet run --project src/EverythingLabeled.Tray
# 或直接运行 Tray 输出目录下的 EverythingLabeled.Tray.exe
```

## 手工验收清单

稀疏包注册完成、Service 与 config 就绪后，建议对齐 [规格 §10](../plans/2026-09-27-explorer-integration.md#10-验收清单规格层) 逐项确认：

- [ ] 稀疏包：子菜单四项（中文项名）；空白处仅「按标签查找」；看标签仅单选；Invoke 即发即忘
- [ ] Host 硬规则：非法丢弃并提示 N；URI 去重；>100 拒绝不截断；DLL 不因数量灰打标/去标
- [ ] PathUri 与 CLI **同一库**；含 UNC / symlink / junction；打标预检排除无效项
- [ ] 启动必做 health（+ meta）；`baseUrl` 仅 127.0.0.1
- [ ] 打标/去标每次一别名；批量并发=`maxConcurrency`（缺省 4）；可取消；401 中止、其它按项继续
- [ ] find：防抖末词 suggest（含简拼）+ 实时 preview；Enter 填联想或打开选中项；无「预览」按钮；Host 屏幕居中；打开后窗口保持；无 expand
- [ ] find UI：深色无边框标题栏；芯片标签 + AND/OR（默认 AND）+ 清空；结果行图标/标题/副标题；打开/再激活自动聚焦；输入框 ↓ 一次进下一项；失焦（点窗外）关闭
- [ ] paths-file 私有 tmp + 读完删除；只读 AppData config（含 `previewLimit` / `maxConcurrency`）
- [ ] Command DLL 不承载业务 HTTP；Host 崩溃不拖死 Explorer（预期）
