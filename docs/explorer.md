# Explorer 上下文菜单（#14）

Windows 稀疏包 `IExplorerCommand` + 进程外 WPF `EverythingLabeled.ExplorerHost`。菜单 DLL 只写 `--paths-file` 并即发即忘启动 Host；业务与 HTTP 全在 Host。规格以 [Explorer 集成](../plans/2026-09-27-explorer-integration.md) 与 [实现计划](../plans/2026-10-07-explorer-impl.md) 为准。

## 工程布局

| 项目 | 说明 |
|---|---|
| `src/EverythingLabeled.ExplorerShared` | paths-file（tmp / Cleanup / Write / ReadAndDelete） |
| `src/EverythingLabeled.ExplorerHost` | WPF Host：`--mode tag\|untag\|tags\|find`，可选 `--paths-file`；exe 图标 `Assets/host.ico`（文件夹 E838 + 标签角标；`scripts/make-host-icon.ps1`） |
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
| **Shared ← Host / Command / Tray** | 三者都 `ProjectReference` Shared。Shared **不**引用另外三者；只提供 paths-file 约定、`FindHostIpc`（Mutex + 命名管道 `Activate\n` → HWND 回写）与 `WindowForeground`（跨进程抢前台）。 |
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

## 命令行启动 Host（六 mode）

Service 已起、`%AppData%\EverythingLabeled\config.json` 已填时，可在不注册稀疏包的情况下自测 Host：

```powershell
# 按标签查找（无选中）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode find

# 管理标签（词库改名 / 删除；不读选中路径；打开后筛选框获焦）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode library

# 管理资源（按标签查找后删除，或替换到新地址；不读选中路径）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode resources

# 看标签（单选；paths-file 一行一个绝对路径）
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode tags --paths-file <paths.txt>

# 打标
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode tag --paths-file <paths.txt>

# 去标
dotnet run --project src/EverythingLabeled.ExplorerHost -- --mode untag --paths-file <paths.txt>
```

`find` / `library` / `resources` 可省略 `--paths-file`；若传入，Host **忽略**其内容（仍会删除误传的 paths-file）。

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

`signtool` 的 `/n` 后跟**裸 CN**（`EverythingLabeled-Dev`）；写成 `CN=EverythingLabeled-Dev` 会匹配不到证书，报 `No certificates were found that met all the given criteria`（证书本身 Subject 才带 `CN=`）。

## 注册（稀疏包 + ExternalLocation）

输出目录（示例）：

`src\EverythingLabeled.ExplorerCommand\bin\Debug\net8.0-windows\`

其中应有：`EverythingLabeled.ExplorerCommand.comhost.dll`、`AppxManifest.xml`、`Assets\`、以及拷贝来的 `EverythingLabeled.ExplorerHost.exe` 与依赖。

`uap10:AllowExternalContent` 包必须用 **ExternalLocation** 指向该输出目录；不能只对 Manifest 做无外部位置的 `-Register`。

### Find 结果行 kind 图标

按标签查找预览列表按 resource `kind` 显示自定义剪影（不再用 Segoe MDL2）：

| kind | 资源键 | 设计稿 |
|---|---|---|
| `file`（及未知） | `KindIconFile` | [icons/file.svg](../design/explorer-ui/icons/file.svg) |
| `folder` | `KindIconFolder` | [icons/folder.svg](../design/explorer-ui/icons/folder.svg) |
| `url` | `KindIconUrl` | [icons/webpage.svg](../design/explorer-ui/icons/webpage.svg) |

几何在 `ExplorerHost/Themes/KindIcons.xaml`；行用 `OpacityMask` + `HostMutedBrush` 着色，选中行改 `HostAccentBrush`。映射单一来源：`FindResultPresentation.IconKey`。

预览列表主行在资源名称后展示该对象已绑定的标签（浅色描边胶囊，与顶部查询芯片区分；过长时名称与标签各自省略）。标签文案来自 `GET /v1/query/preview` 每条 hit 的 `tags`（按资源绑定顺序；每个标签取字典序首个别名）。`GET /v1/query` 仍不返回 `tags`。见 [find.html 填充态](../design/explorer-ui/find.html?state=filled)。

### 管理资源查询

芯片查询与 Find 相同：`findTagLogic` 决定 AND/OR；末词防抖 suggest（含简拼 `NameInitials`）；Enter 落下高亮联想（无联想才把草稿当别名）。preview 引擎仍不认简拼，不能靠把拼首当芯片去查资源。空状态提示在结果区水平垂直居中，底栏「先添加标签」；有草稿时底栏为「建议 N 条」。设计稿 → [resources.html](../design/explorer-ui/resources.html)。

### 菜单图标

六项各有一个青绿 `#2EC4B6` 字形图标，由 `ExplorerCommandBase.GetIcon` 返回**绝对路径**：

| 菜单项 | mode | Glyph | 文件 |
|---|---|---|---|
| 打标 | `tag` | `E8EC` | `Assets\ElTag.ico` |
| 去标 | `untag` | `E74D` | `Assets\ElUntag.ico` |
| 看标签 | `tags` | `E8FD` | `Assets\ElTags.ico` |
| 管理标签 | `library` | `E8F1` | `Assets\ElLibrary.ico` |
| 管理资源 | `resources` | `E8A5` | `Assets\ElResources.ico` |
| 按标签查找 | `find` | `E721` | `Assets\ElFind.ico` |

- 图标资源在 `src\EverythingLabeled.ExplorerCommand\Package\Assets\`，由 [scripts/make-explorer-icons.ps1](../scripts/make-explorer-icons.ps1) 生成（16/20/24/32/48 多尺寸、DIB 帧、透明底），该脚本同时用 `el.ico` 缩放生成清单要的三个 Logo（**不要**把它们留成纯黑占位图 —— 命令若拿不到图标，壳层会回落到包 Logo，黑图就显示成黑方块）。
- `mode → 文件名` 的映射与路径解析在 `ExplorerShared/ExplorerIcons.cs`；「菜单扩展自己所在目录」的解析在 `ExplorerCommand/CommandDirectory.cs`（`HostLauncher` 共用同一个）。
- `find` 用放大镜 `E721`，与 Host 标题栏的 `E8B7`（画出来是纸/文档）**故意不同** —— 那个字形当"查找"不对。
- 必须绝对路径：包走 `ExternalLocation`，COM 服务器跑在壳层 surrogate 里，相对路径无从解析。图标缺失时 `GetIcon` 退回 `E_NOTIMPL`（当没图标），**不能让异常冒出去** —— 那会让壳层把整项菜单丢掉。

改字号/改色后：重跑生成脚本 → `dotnet build` → 递增清单版本 → `register-explorer.ps1`。打包链路（csproj 通配 + 脚本 `Copy-Item Assets\*`）自动带上新文件，无需改。

### 两个必守的前提

1. **打包前必须 `dotnet build`**：打包读的是输出目录里的 `AppxManifest.xml`，而它是**构建产物**（csproj 用 `CopyToOutputDirectory="PreserveNewest"` 从 `Package\` 拷出）。源码改过 / 回滚过但没重新构建时，会把**上一次**的旧清单打进包 —— 注册"成功"、右键却还是旧内容（甚至指向已不存在的 DLL）。
2. **清单 `Version` 必须递增**：`Add-AppxPackage` 不允许降级，已装版本 ≥ 清单版本会报 `0x80073D06`。`ProcessorArchitecture` 用 `neutral`（与 PowerToys 那批右键菜单稀疏包一致）。

### 一键注册（推荐）

`scripts\register-explorer.ps1` 把上面两条与打包 / 签名 / 注册 / 回读校验串起来：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\register-explorer.ps1
```

流程：构建 → 校验输出清单与源码一致（不一致则覆盖并告警）→ 校验清单声明的 `Executable` 与 `com:Class Path` 在输出目录里真的存在 → **对每个 CLSID `CoCreate`**（失败含 `0x80040111` 则中止）→ 比版本 → `makeappx pack` + `signtool sign` → `Add-AppxPackage -ExternalLocation` → 回读 `PackagedCom` 打印每个 CLSID 实际解析到的 DLL。

### 手工步骤（与脚本等价）

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

注册后：资源管理器右键文件/文件夹应见应用名 **EverythingLabeled** 下的「打标 / 去标 / 看标签 / 管理标签 / 管理资源 / 按标签查找」；文件夹空白处为「管理标签」「管理资源」「按标签查找」。菜单 Invoke **不**等待 Host 退出。

若菜单未出现：结束并重启 `explorer.exe`，或注销再登录。

排查时按「全没有」和「只少新增的一项」分开。注销、重启 Explorer **修不好**第二种。

- `Get-AppxPackage -Name EverythingLabeled.ExplorerDev` 的 `Version` 是否等于刚打的清单版本（对不上 = 打到过期产物）。
- `HKLM\SOFTWARE\Classes\PackagedCom\Package\<PackageFullName>\Class\{CLSID}` 的 `DllPath` 指向的 DLL 是否存在于 ExternalLocation（不存在 = Explorer 创建 COM 失败，会**静默丢弃**该菜单项）。
- **一项都没有**：看壳层是否用 HKCU 空 `InprocServer32` 覆盖了 `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}`（经典右键菜单 hack）。packed 项会被**全部丢弃**；删掉该键并重启 `explorer.exe` 即恢复（2026-10-16 本机实测确认）→ [排查存档与恢复](../plans/2026-10-16-explorer-menu-missing.md)。
- **旧项都在、只有刚加的那一项没有**（管理标签、管理资源都遇过）：清单、包版本、`DllPath` 文件存在都对，仍会这样。对本机 `CoCreate` 缺失项的 CLSID。`0x80040111`（`CLASS_E_CLASSNOTAVAILABLE`）= `bin\…\EverythingLabeled.ExplorerCommand.comhost.dll` 是旧宿主。`CreateComHost` 把 clsidmap 嵌进 `obj` 里的 comhost **不改时间戳**，增量拷贝按时间跳过，壳层 `ExternalLocation` 一直加载少一个类的那份。对照两边文件大小（2026-10-20：bin 195072、obj 195584），或在 bin 里搜新类名。工程已用 `CopyEmbeddedComHostToOutput` 强制覆盖；注册脚本打包前会 `CoCreate` 每个 CLSID。步骤与当次证据 → [comhost 过期存档](../plans/2026-10-20-explorer-comhost-stale.md)。

## 卸载

```powershell
Get-AppxPackage -Name EverythingLabeled.ExplorerDev | Remove-AppxPackage
```

包全名以 `Get-AppxPackage` 输出为准（含版本与 Publisher 哈希后缀）。

## 依赖

- **Service**：本机已监听 `127.0.0.1`，Host 经 Bearer 调 `/v1`（health 失败则 UI 提示，业务不可用）。
- **`%AppData%\EverythingLabeled\config.json`**：Host **只读**、**不写回**；忽略 `EL_*` 环境变量。与 CLI 共用同一文件。完整键表见 [本机配置](./config.md)。Host 常用：`previewLimit`（标签查询预览条数，缺省 16）、`suggestLimit`（拼首联想预览条数，缺省 8；打标 / Find / 管理资源 suggest）、`maxConcurrency`、`confirmUntag`、`findTagLogic`（Find 多标签 AND/OR，改后重启 Host）；`findHotkey` / `runAtStartup` 由 Tray 读，见 [Tray 规格](../plans/2026-10-07-tray-find-hotkey.md)。与扩展 Options 同名两键对齐，缺省/上限见 config 表。

### Tray（Find 热键）

- 工程：`src/EverythingLabeled.Tray`。输出目录须有同目录 `EverythingLabeled.ExplorerHost.exe`（构建已自动拷贝）。
- 默认热键 `Alt+L`（config `findHotkey`；空串/`none` 关热键；改 JSON 后重启 Tray）；菜单可「打开查找」。右键「重新启动」结束全部 `EverythingLabeled.ExplorerHost` 后冷启动 Find（重新做启动探测；也会关掉当时开着的打标/管理窗口）。
- HKCU Run 值名 `EverythingLabeled.Tray`（与 Service 的 `EverythingLabeled` 并存）；`runAtStartup` 默认 true，托盘可勾选切换。
- 首次需手动启动一次 Tray（本切片无安装器自动拉起）。
- Find **全入口单实例**（热键 / 托盘 / 右键 / CLI）：Mutex + 管道 `Activate\n`（服务端回写 HWND；Tray 在热键上下文 `AllowSetForegroundWindow` + `SetForegroundWindow`）；关窗/失焦 **Hide** 保活，再按热键毫秒级 Activate；退出 Tray **不**杀已开 Host。服务晚于 Find 进程起来时，横幅会停在「服务未运行或地址错误」，用托盘「重新启动」清掉。

```powershell
dotnet run --project src/EverythingLabeled.Tray
# 或直接运行 Tray 输出目录下的 EverythingLabeled.Tray.exe
```

## 手工验收清单

稀疏包注册完成、Service 与 config 就绪后，建议对齐 [规格 §10](../plans/2026-09-27-explorer-integration.md#10-验收清单规格层) 逐项确认：

- [ ] 稀疏包：子菜单五项（打标/去标/看标签/管理标签/按标签查找）；空白处「管理标签」+「按标签查找」；看标签仅单选；Invoke 即发即忘
- [ ] 稀疏包：五项各显示青绿字形图标（含管理标签 `E8F1`），**不是黑方块**
- [ ] Host 硬规则：非法丢弃并提示 N；URI 去重；>100 拒绝不截断；DLL 不因数量灰打标/去标
- [ ] PathUri 与 CLI **同一库**；含 UNC / symlink / junction；打标预检排除无效项
- [ ] 启动必做 health（+ meta）；`baseUrl` 仅 127.0.0.1
- [ ] 打标/去标每次一别名；批量并发=`maxConcurrency`（缺省 4）；可取消；401 中止、其它按项继续
- [ ] find：防抖末词 suggest（`suggestLimit`，含简拼）+ 实时 preview（`previewLimit`）；Enter 填联想或打开选中项；无「预览」按钮；Host 屏幕居中；打开后窗口保持；无 expand
- [ ] resources：空状态居中；拼首/前缀联想后 Enter 成芯片再 preview；行内删除/替换
- [ ] find UI：深色无边框标题栏；芯片标签 + AND/OR（默认 AND）+ 清空；结果行按 kind 显示文件/文件夹/网页图标（`Themes/KindIcons.xaml`，对齐 [design/explorer-ui/icons](../design/explorer-ui/icons.html)）；打开/再激活自动聚焦；输入框 ↓ 一次进下一项；失焦或关窗只隐藏（进程保活，再按热键毫秒唤起）
- [ ] 打标/去标/看标签/管理标签 UI：深色无边框与 Find 同主题（`Themes/HostDark.xaml`）；点击窗口外失焦自动关闭（Find 为 Hide 保活）；别名联想浮动下拉不撑布局；打标时无精确匹配则自动勾选「创建缺失标签」；打标页底部不显示「取消」「打标」，别名框 Enter 直接提交（有联想时用当前高亮项）；打标在单选时于目标路径和别名之间显示当前标签（带 × 芯片，点叉号去除），多选打标不显示该区；去标页不显示别名与底部「取消」「去标」，单选当前标签可检索后点叉号去除，多选提示只选一个路径 → [design/explorer-ui/](../design/explorer-ui/index.html)
- [ ] paths-file 私有 tmp + 读完删除；只读 AppData config（含 `previewLimit` / `suggestLimit` / `maxConcurrency`）
- [ ] suggest：`config.json` 改 `suggestLimit` 为 3 后重启 Host，打标 / Find / 管理资源拼首联想最多 3 条
- [ ] Command DLL 不承载业务 HTTP；Host 崩溃不拖死 Explorer（预期）
