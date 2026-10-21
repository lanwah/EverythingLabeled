# 新增右键项不出现：bin 里的 comhost.dll 是旧的

日期：2026-10-20  
状态：**已收口**（2026-10-20）—— 输出目录强制覆盖已嵌入 clsidmap 的 `comhost.dll`；`register-explorer.ps1` 打包前对每个 CLSID 做 `CoCreate`  
关联：[docs/explorer.md](../docs/explorer.md) · [全部菜单都不出现](./2026-10-16-explorer-menu-missing.md)

本问题在**每新增一个 COM 菜单类**时都会再踩（管理标签、管理资源都遇过）。注销、重启 `explorer.exe` 修不好。

## 1. 现象

稀疏包已是新版本（本次 `EverythingLabeled.ExplorerDev` **0.1.10.0**，`Status: Ok`），清单和 `PackagedCom` 都有新动词（本次 `ElResources` / `{6F3C8A21-9B4E-4D71-A2C8-1E5F0D8B7A06}`），旧菜单（打标 / 去标 / 看标签 / 管理标签 / 按标签查找）都在，**只有刚加的那一项没有**。注销后再看仍然没有。

和 [2026-10-16 全部不显示](./2026-10-16-explorer-menu-missing.md) 分开：那次是过期清单或 HKCU 经典菜单覆盖，**所有** packed 项一起消失。这次是壳层创建**某一个** CLSID 失败，只丢掉那一项。

## 2. 证据（2026-10-20 本机）

| | `bin\Debug\net8.0-windows\EverythingLabeled.ExplorerCommand.comhost.dll`（壳层实际加载） | `obj\Debug\net8.0-windows\` 同名文件 |
|---|---|---|
| 大小 | 195072 | 195584 |
| 时间戳 | 2026-04-29 23:38:22（SDK 自带宿主的时间，嵌入 clsidmap **不改**） | 同样是 2026-04-29 23:38:22 |
| 内含 | 有 `LibraryCommand`，**没有** `ResourcesCommand` / `1e5f0d8b7a06` | 有 `ResourcesCommand` 与 `1e5f0d8b7a06` |

同目录的 `EverythingLabeled.ExplorerCommand.clsidmap`（1559 字节，2026-10-20 20:21）和托管 DLL **已经**包含 `ResourcesCommand`。缺的只是输出目录那份原生宿主。

`CoCreateInstance`（`CLSCTX_ALL`，`IUnknown`）：

```text
{…7A04} Find     hr=0x00000000
{…7A05} Library  hr=0x00000000
{…7A06} Resources hr=0x80040111   CLASS_E_CLASSNOTAVAILABLE
```

Explorer 创建失败会**静默丢掉该菜单项**，其它项照常。

## 3. 机制

`EnableComHosting` 的 `CreateComHost` 把 `*.clsidmap` **嵌进** `obj\…\*.comhost.dll`。嵌入不更新 `LastWriteTime`。随后 `CopyFilesToOutputDirectory` 按时间戳跳过「未变化」文件，`bin` 里留下上一次少一个类的宿主。

稀疏包 `ExternalLocation` 指向 `bin`。壳层加载的是这份旧宿主，所以：

- `Get-AppxPackage` 版本对、安装清单里有 Verb、`PackagedCom` 的 `DllPath` 文件也存在 —— 这三处都**不能**排除本问题。
- 注销 / 重启 Explorer 只是重新加载同一份旧 DLL。

## 4. 下次怎么确认

在 `src\EverythingLabeled.ExplorerCommand` 下：

```powershell
$out = 'bin\Debug\net8.0-windows\EverythingLabeled.ExplorerCommand.comhost.dll'
$mid = 'obj\Debug\net8.0-windows\EverythingLabeled.ExplorerCommand.comhost.dll'
Get-Item $out, $mid | Select-Object FullName, Length, LastWriteTime
# bin 更小，或 ASCII 里没有新类名（如 ResourcesCommand）= 拷贝被跳过
```

再对**缺失那一项**的 CLSID 做 `CoCreate`。`0x80040111` 即该类没进当前加载的宿主。旧 CLSID 仍返回 `0`。

## 5. 处置（已进仓库）

| 位置 | 做什么 |
|---|---|
| `EverythingLabeled.ExplorerCommand.csproj` 的 `CopyEmbeddedComHostToOutput` | `CopyFilesToOutputDirectory` 之后，把 `$(IntermediateOutputPath)*.comhost.dll` **强制**覆盖到输出目录（`SkipUnchangedFiles=false`） |
| `scripts/register-explorer.ps1` | 打包前对清单里每个 `com:Class` 做 `CoCreate`；非 0（含 `0x80040111`）直接失败，避免再注册一个「文件在、类不在」的包 |

当时本机：重建后 `bin` 与 `obj` 同为 195584 字节，且内含 `ResourcesCommand`；A04 / A05 / A06 的 `CoCreate` 均为 `0`。然后重启了 `explorer.exe`。

新增菜单类之后：`dotnet build`（或直接跑 `register-explorer.ps1`）即可；不要只看 `comhost.dll` 的修改时间。
