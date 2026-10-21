# Explorer 右键菜单不显示：排查存档与恢复

日期：2026-10-16  
状态：**已收口**（2026-10-16）—— 两层原因都已坐实并处置：① 打到过期构建产物（已修）；② 壳层 HKCU 经典菜单覆盖（已移除，**四菜单实测出现**）  
关联：

- [Explorer 集成规格](./2026-09-27-explorer-integration.md) · [Explorer 实现计划](./2026-10-07-explorer-impl.md) · [docs/explorer.md](../docs/explorer.md)
- 直接触发：`f18e293 #109`「由于系统升级之后右键菜单注册不上，因此回滚了代码到 108 的状态」
- 环境：Windows 11 **26H2 / build 26300.9711**（`DisplayVersion=26H2`，`CurrentBuild=26300`）
- 后续：本文 §3.1 的 as-built（包 `0.1.7.0`）是 **#110 当时的落地状态**；包版本随后已提到 **`0.1.8.0`**（菜单图标）→ [菜单图标存档](./2026-10-17-explorer-menu-icons.md)

## 1. 现象

稀疏包 `EverythingLabeled.ExplorerDev` 按 [docs/explorer.md](../docs/explorer.md) 注册"成功"（`Get-AppxPackage` 有包、`Status: Ok`、`SignatureKind: Developer`），但资源管理器右键**文件与文件夹都没有**「打标 / 去标 / 看标签 / 按标签查找」四项。

## 2. 排查与证据

### 2.1 已证实（并已修复）：打出去的是过期构建产物

打包脚本读的是**输出目录里的** `bin\...\AppxManifest.xml`，而它是构建产物（csproj `CopyToOutputDirectory="PreserveNewest"` 从 `Package\` 拷出）。#109 回滚源码后**没有重新 build**，于是把 10-08 遗留的本机实验清单打进了包：

| | 注册到的（旧） | 源码 `Package/AppxManifest.xml` |
|---|---|---|
| Version | `0.1.6.0` | `0.1.0.0`（回滚后） |
| ProcessorArchitecture | `neutral` | `x64` |
| com:Class | 1 个（A01），`Path="ElShell.dll"` | 4 个（A01–A04），`comhost.dll` |
| Verb | 只有 `ElTag` / `ElTagDir` | 9 个（四模式 + 目录变体 + 空白处 find） |

铁证（`HKLM\SOFTWARE\Classes\PackagedCom\Package\<PackageFullName>\Class\{CLSID}`）：

```text
…EverythingLabeled.ExplorerDev_0.1.6.0_neutral__2tvmsqdk4aexr\Class\{6F3C8A21-…-1E5F0D8B7A01}
    DllPath = ElShell.dll          ← bin 里根本没有这个文件
```

即：去标/看标签/按标签查找**从未注册**；打标注册了但 COM 服务器指向不存在的 DLL → Explorer 创建失败 → **静默丢弃**该菜单项。

**处置**：清单提到 `0.1.7.0` / `neutral` / 补 `uap10:RuntimeBehavior="packagedClassicApp"`（对齐 PowerToys 那批在用的稀疏包），重建 + 重签 + 重装为 `EverythingLabeled.ExplorerDev_0.1.7.0_neutral__2tvmsqdk4aexr`，回读 `PackagedCom` 四个 CLSID 全部指向存在的 `EverythingLabeled.ExplorerCommand.comhost.dll` [OK]。详见 §4.3。

> 另一个附带结论：`Add-AppxPackage` 不允许降级。已装 `0.1.6.0` 时注册回滚后的 `0.1.0.0` 会报 `0x80073D06` —— 这很可能就是 #109 里"系统升级之后注册不上"的机制。

### 2.2 已排除

| 怀疑 | 结论 |
|---|---|
| 服务（Service）没开导致菜单不渲染 | **无关**。菜单枚举只调 `GetTitle`/`GetState`/`GetFlags`，全硬编码、零 HTTP；`ExplorerCommand` 工程连 `Client` 都没引用。Service 挂了只影响**点下去之后**（Host 起窗 + 「服务不可达」横幅 + 业务禁用，`App.xaml.cs:67-71`） |
| 证书吊销/丢失 | `CN=EverythingLabeled-Dev` 在 `CurrentUser\My`（有私钥、EKU=Code Signing）与 `LocalMachine\Root` 均在 |
| Windows Kits 工具缺失 | `makeappx.exe` / `signtool.exe` 在 10.0.17763 / 19041 / 26100 三处都有 |
| 注入型壳层工具 | 未安装 ExplorerPatcher / StartAllBack（`HKLM\SOFTWARE\ExplorerPatcher`、`StartIsBack*` 均不存在；`Winlogon\Shell = explorer.exe` 正常） |

顺带记录两个踩过的坑：

- `signtool sign /n` 只认**裸 CN**（`EverythingLabeled-Dev`）；写成 `CN=EverythingLabeled-Dev` 会匹配不到证书，报 `No certificates were found that met all the given criteria`（证书本身的 Subject 才带 `CN=`）。
- 本机从 Git Bash 调 `powershell -NoProfile` 时 **`Cert:` 盘与 `Get-FileHash` 不可用**（报 `DriveNotFoundException` / `CommandNotFoundException`，`$PSModulePath` 看着正常）。查证书与私钥改走 `certutil -store -user My <thumbprint>` 或 .NET（`X509Store` + `RSACertificateExtensions.GetRSAPrivateKey`）；比对文件改走 `[System.IO.File]::ReadAllText`。`scripts/register-explorer.ps1` 因此没用 `Get-FileHash`。

### 2.3 根因②（已确认）：本机 HKCU 覆盖了 Win11 新菜单的 CLSID

```text
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
    [(default)] = <>          ← 空值
```

`{86ca1aa0-…}` 是 **Windows 11 新右键菜单的处理器 CLSID**；在 HKCU 建一个空的 `InprocServer32` 是流传极广的「恢复 Win10 经典右键菜单」hack（HKLM 下的同名键是真实注册，被 HKCU 这份盖掉）。

为什么这能解释我们：四项全是 **packed（稀疏包 `IExplorerCommand`）**，只走新菜单的枚举路径。经典菜单那条老路径捡 packed verbs 一向不可靠 —— 微软 Q&A 有专帖记录：同一 `Clsid`/`Verb` 在新菜单里有、在经典「显示更多选项」里**随机没有**，且随机器上装了哪些 packed 扩展而变，属未文档化行为。

**2026-10-16 实测确认**：键内容备份留档后删掉该覆盖并重启 Explorer（23:24:12），右键文件/文件夹**立即出现**四项 —— 根因坐实。（备份：`%TEMP%\el-classic-menu-hack-backup.reg`；`reg export` 首次因 `HKCU:\` 写法失败，该 .reg 是按删前打印的已知内容重建的，`reg import` 冒烟过。还原两条命令已回环验证。）

### 2.4 次层风险（**已排除**：去掉覆盖后菜单即出现，未触发）

- 本机 `26300.9711 / 26H2` 比社区报告的「25H2 更新后自定义右键项消失」那批（`26200.8521` → `26200.8875` 起，影响 `Explorer\CommandStore` 之外创建的项）更新，可能另有一层壳层回归。
- StackOverflow 有 sparse package 开发者报告**完全同款症状**：配置/签名/CLSID 都对，菜单先显示后消失，右键会闪一行空白再收掉，帖内无定论。

## 3. 结论

菜单不显示是**两层原因叠加**，缺一不可地都要修：

1. **注册侧（已修，§2.1）**：包是用过期构建产物打的 —— 清单还是 10-08 的本机实验版（只 1 个 CLSID、`Path="ElShell.dll"`），而 `bin` 里没有 `ElShell.dll`，于是「去打标的 COM 服务器」根本创建不起来；另外三项压根没注册。修法：重建重装 `0.1.7.0`/neutral + 一键注册脚本。
2. **壳层（已修，§2.3）**：HKCU 用空 `InprocServer32` 覆盖了 Win11 新菜单的处理器 CLSID。packed（稀疏包 `IExplorerCommand`）项只走新菜单枚举路径，被覆盖后**全部不显示** —— 这才是"修好注册后仍然一项都没有"的原因。去掉覆盖 + 重启 Explorer 后四菜单实测出现。

**遗留的取舍**：经典菜单覆盖与 packed 右键项**不可兼得**。当前选择是去掉覆盖、使用 Win11 新式菜单。若要长期保留经典菜单，需要把四项改走 legacy `IContextMenu`（进程内 shell extension）方案 —— 未在本切片内。

**附带结论**：`Add-AppxPackage` 不允许降级（已装 `0.1.6.0` 时注册 `0.1.0.0` 报 `0x80073D06`），这很可能就是 `f18e293 #109`「系统升级之后右键菜单注册不上」的机制。

### 3.1 收口时的落地状态（as-built）

| 项 | 状态 |
|---|---|
| 注册包 | `EverythingLabeled.ExplorerDev_0.1.7.0_neutral__2tvmsqdk4aexr`，`Status: Ok` / `SignatureKind: Developer` |
| PackagedCom | A01–A04 → `EverythingLabeled.ExplorerCommand.comhost.dll`（四个 DllPath 均存在） |
| 壳层 | HKCU `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` 覆盖：**已移除** |
| 清单 | `Package/AppxManifest.xml` = `0.1.7.0` / `neutral` / `com:Extension` 带 `packagedClassicApp` |
| 实测 | 右键文件与文件夹可见四项（2026-10-16 23:24 后） |

## 4. 恢复（回退）方案

### 4.1 壳层：经典菜单覆盖（双向）

> A/B 已于 2026-10-16 做完并坐实（见 §2.3/§3.1）；下面是可复用的命令本身，双向都在。

先做 A/B（不动任何东西）：① 看菜单长相是 Win11 新式（窄、带图标）还是 Win10 老式（长、带"显示更多选项"的是新式）；② 看 **Bandizip / PowerToys 的右键项**是否也不见 —— 它们同样是 packed，若也不见即锁死壳层。

```powershell
# 备份现状
reg export "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" "$env:TEMP\win11-menu-backup.reg" /y

# 【测试/恢复新版菜单】去掉覆盖
Remove-Item "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" -Recurse -Force
Stop-Process -Name explorer -Force      # Explorer 会自动重启

# 【还原经典菜单】把空键加回去
New-Item -Path "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32" -Name '(default)' -Value ''
Stop-Process -Name explorer -Force
```

### 4.2 稀疏包：卸载 / 重装

```powershell
Get-AppxPackage -Name EverythingLabeled.ExplorerDev | Remove-AppxPackage      # 卸载
powershell -ExecutionPolicy Bypass -File scripts\register-explorer.ps1        # 重装（会自动 build）
```

### 4.3 代码：本次改动及其回退

| 文件 | 改动 | 回退 |
|---|---|---|
| `src/EverythingLabeled.ExplorerCommand/Package/AppxManifest.xml` | `Version` 0.1.0.0→**0.1.7.0**；`ProcessorArchitecture` x64→**neutral**；`com:Extension` 补 `uap10:RuntimeBehavior="packagedClassicApp"` | `git checkout -- <file>`。⚠️ 回退前先卸包：`Version` 退回 0.1.0.0 会被降级检查拒掉（`0x80073D06`） |
| `scripts/register-explorer.ps1` | 新增：build → 校验输出清单与源码一致 → 校验清单声明的 `Executable`/`com:Class Path` 真实存在 → 比版本 → pack/sign/register → 回读 `PackagedCom` | 删除该文件（纯新增，无副作用） |
| `docs/explorer.md` | 新增「两个必守的前提」「一键注册」「排查两看」；补 `signtool /n` 裸 CN 提示 | `git checkout -- docs/explorer.md` |
| `project.md` | 看板补一行 | `git checkout -- project.md` |
| `plans/done.md` | 追一条 2026-10-16 记录 | `git checkout -- plans/done.md` |
| `plans/2026-10-16-explorer-menu-missing.md`（本文） | 存档 | 删除 |

## 5. 收口与遗留

已做：

- [x] 按 §4.1 去掉 HKCU 覆盖 → 重启 Explorer → 四项出现（2026-10-16 23:24）
- [x] 结论回写本文件（状态改「已收口」）、[docs/explorer.md](../docs/explorer.md)、[project.md](../project.md)、[done.md](./done.md)

遗留：

- [ ] **取舍**：经典菜单与 packed 右键项不可兼得 —— 当前选新式菜单。若要经典菜单，需改走 legacy `IContextMenu`（另开切片/规格）
- [ ] 本机手工验收 Explorer Host：打标 / 去标 / 看标签深色皮肤（[design/explorer-ui/](../design/explorer-ui/index.html)；联想浮动；缺失别名自动勾选创建缺失）
- [ ] 下次再遇"菜单不见"，先分两种：全部没有 → 跑 [scripts/register-explorer.ps1](../scripts/register-explorer.ps1)，再查 §2.3 那个键在不在；只有新增的一项没有 → [comhost 时间戳被跳过](./2026-10-20-explorer-comhost-stale.md)（`CoCreate` = `0x80040111`）

## 6. 参考

- [MS Q&A — Windows 11 classic context menu weird handling of IExplorerCommand](https://learn.microsoft.com/en-nz/answers/questions/5979037/windows-11-classic-context-menu-weird-handling-of)
- [ElevenForum — Changes in context menu functionality](https://www.elevenforum.com/t/changes-in-context-menu-functionality.49492/)
- [StackOverflow — How to debug a Windows 11 context menu display?](https://stackoverflow.com/questions/79746783)
- [4sysops — Restore classic context menu in Windows 11 Explorer using Group Policy or PowerShell](https://4sysops.com/archives/restore-classic-context-menu-in-windows-11-explorer-using-group-policy-or-powershell/)
