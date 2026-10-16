# Explorer 右键菜单图标：黑方块 → 四个字形图标

日期：2026-10-17
状态：**已收口**（2026-10-17）—— 菜单四项已各带图标，包 `0.1.8.0` 已重装
关联：

- [docs/explorer.md](../docs/explorer.md) · [scripts/make-explorer-icons.ps1](../scripts/make-explorer-icons.ps1)
- 前置：[Explorer 右键菜单不显示：排查存档与恢复](./2026-10-16-explorer-menu-missing.md)（#110 把四项恢复出来，本次修的是图标）
- 环境：Windows 11 **26H2 / build 26300**（新式菜单；HKCU 经典菜单覆盖已于 #110 移除）

## 1. 现象

#110 之后四项（打标 / 去标 / 看标签 / 按标签查找）显示正常，但每项前面的图标是**纯黑方块**。

## 2. 根因（两层，缺一不可）

### 2.1 命令没提供图标

`ExplorerCommandBase.GetIcon` 以前把 `ppszIcon` 置 `null` 后直接返回 `E_NOTIMPL`（"没有图标"），四个动词都一样。

### 2.2 包清单里的占位 Logo 本身就是黑图

`ExplorerCommand/Package/Assets/` 下三个 Logo **md5 完全相同**（`657062fb1af772dec6521651eef69865`），都是 50×50、`Format24bppRgb`（无 alpha）的**纯黑**图 —— 逐像素采样确认四点全为 `A255 R0 G0 B0`。

命令不给图标时壳层**回落到包 Logo**，于是显示成一个黑方块。（回落这一点无权威文档，但两处证据指向它：包清单 Logo 确实是黑的、且命令侧确实没给图标。两条路本次都修了，任一为真都能好。）

## 3. 处置

### 3.1 四个动词图标

[scripts/make-explorer-icons.ps1](../scripts/make-explorer-icons.ps1) 用 `System.Drawing` + `Segoe MDL2 Assets` 生成到 `Package/Assets/`，青绿 `#2EC4B6`、透明底、尺寸 **16/20/24/32/48**：

| 菜单项 | mode | Glyph | 文件 |
|---|---|---|---|
| 打标 | `tag` | `E8EC` | `ElTag.ico` |
| 去标 | `untag` | `E74D` | `ElUntag.ico` |
| 看标签 | `tags` | `E8FD` | `ElTags.ico` |
| 按标签查找 | `find` | **`E721`**（放大镜） | `ElFind.ico` |

前三个与 Host 标题栏走位（`ExplorerHost/MainWindow.xaml.cs`）同源。**`find` 故意与 Host 不同**：Host 标题栏用的是 `E8B7`，但那个字形画出来是一页纸/文档，当"查找"不对；放大镜 `E721` 才是正解，也是设计稿搜索框（`design/explorer-ui/find.html`）用的字形。

### 3.2 三个包 Logo

同一脚本从 `ExplorerShared/Assets/el.ico` 的 256 帧缩放生成 `Square44x44Logo.png`(44)、`Square150x150Logo.png`(150)、`StoreLogo.png`(50)，替掉黑图（清单 `BackgroundColor="transparent"` 不变）。

### 3.3 `GetIcon` 与路径解析

- `ExplorerShared/ExplorerIcons.cs`（新增）：`FileNameForMode(mode)`（未知 mode → `null`，不回落默认图）+ `Resolve(directory, mode)`（拼 `Assets\<文件>`，不存在返回 `null` 不抛）。
- `ExplorerCommand/CommandDirectory.cs`（新增）：把原先 `HostLauncher.ResolveCommandDirectory()` 的那段解析提出来 —— `Assembly.Location` → 目录、退化到 `AppContext.BaseDirectory`。`HostLauncher` 与 `GetIcon` 共用一份（DRY）。
- `ExplorerCommandBase.GetIcon`：`ExplorerIcons.Resolve(CommandDirectory.Resolve(), Mode)`，命中返回 `S_OK` + **绝对路径**，否则维持 `E_NOTIMPL`。整段包 try/catch —— 图标出岔子**绝不能**让壳层把整项菜单丢掉（#109 的教训）。
- 绝对路径是硬要求：包走 `ExternalLocation`，COM 跑在壳层 surrogate 里，相对路径与 `AppContext.BaseDirectory` 都不可依赖。

### 3.4 清单版本

`0.1.7.0` → **`0.1.8.0`**（`Add-AppxPackage` 不接受降级）。构建通配 `Package\Assets\**\*` 与注册脚本的 `Copy-Item $out\Assets\*` 都已覆盖新文件，**打包链路无需改动**。

## 4. 踩过的坑（复查时有用的记录）

| 坑 | 现象 | 结论 |
|---|---|---|
| **PNG 帧 vs DIB 帧** | 先照 `el.ico` 的写法出 PNG 帧，`System.Drawing.Icon.ToBitmap()` 对 16/20/24/32 帧解出**乱码**（el.ico 16px 本应透明的角读成 `A33 R88 G177`），48/256 帧直接抛异常 | 新图标一律用 **DIB（BITMAPINFOHEADER + 32bpp BGRA + 全零 AND 掩码）** 帧。PNG-in-ICO 只有 256 尺寸是官方推荐；≤48 用 DIB 才是各消费端都稳的编码。`el.ico` 六帧全是 PNG 帧 —— GDI `LoadImage` 能吃（实测 ok），但 `Icon.ToBitmap()` 这类 GDI+ 消费端会翻车，属既有隐患，本次未动 |
| **PowerShell 返回值拆包** | 生成的 `.ico` 只有 **91 字节**（目录里写着 1128/1720/… 的帧长，文件却只写了每帧 1 字节） | `return $byteArray` 会被 PowerShell 拆成一串对象，`BinaryWriter.Write` 于是只写 1 个字节。用 **`return ,$bytes`** 保住 `byte[]` |
| **PS 5.1 读脚本编码** | 新脚本中文全变乱码 + 语法报错 | PowerShell 5.1 按 ANSI 读**无 BOM** 的 UTF-8 脚本。仓库里的 `.ps1` 必须存成 **UTF-8 with BOM**（`register-explorer.ps1` 就是），否则中文注释/字符串全烂 |
| **16px 超采样反而更糊** | 4× 超采样再双三次降采样，16px 下发虚 | 直接按目标尺寸画 + 纯 `AntiAlias` 最清晰（双三次降采样把描边泡开了） |
| **Shift+F10 不是新菜单** | 键盘 `Shift+F10` 弹的是**经典**菜单（剪切/复制/粘贴），packed 项在这条路上不可靠 | 要看 Win11 新式菜单必须用**鼠标右键**；验证时注意这点 |

## 5. 回退

| 文件 | 改动 | 回退 |
|---|---|---|
| `Package/AppxManifest.xml` | `Version` 0.1.7.0→**0.1.8.0** | `git checkout -- <file>`；⚠️ 先卸包，降级会被拒（`0x80073D06`） |
| `Package/Assets/{ElTag,ElUntag,ElTags,ElFind}.ico` | 新增 | 删除四个文件 |
| `Package/Assets/{Square44x44Logo,Square150x150Logo,StoreLogo}.png` | 换成真图 | `git checkout -- src/EverythingLabeled.ExplorerCommand/Package/Assets` |
| `ExplorerShared/ExplorerIcons.cs` | 新增 | 删文件（`GetIcon` 回退后不再引用） |
| `ExplorerCommand/CommandDirectory.cs` | 新增（抽出复用） | 删文件并还原 `HostLauncher.cs` |
| `ExplorerCommand/ExplorerCommandBase.cs` | 实现 `GetIcon` | `git checkout -- <file>`（回到返回 `E_NOTIMPL`） |
| `scripts/make-explorer-icons.ps1` | 新增（纯生成器，不进包） | 删文件 |
| `tests/.../ExplorerIconsTests.cs` | 新增 | 删文件 |
| `docs/explorer.md` · `project.md` · `plans/done.md` | 图标节与看板 | `git checkout -- <file>` |

重装/卸载：

```powershell
Get-AppxPackage -Name EverythingLabeled.ExplorerDev | Remove-AppxPackage   # 卸载
powershell -ExecutionPolicy Bypass -File scripts\register-explorer.ps1     # 重装
```

## 6. 收口时的落地状态（as-built）

| 项 | 状态 |
|---|---|
| 注册包 | `EverythingLabeled.ExplorerDev_0.1.8.0_neutral__2tvmsqdk4aexr` |
| PackagedCom | A01–A04 → `EverythingLabeled.ExplorerCommand.comhost.dll`（四个 DllPath 均存在 `[OK]`） |
| 包内 Assets | `ElTag/ElUntag/ElTags/ElFind.ico` + 三个真 Logo（makeappx 打包日志已列出） |
| 图标文件 | 各 5 帧（16/20/24/32/48），DIB 帧，19278 字节；GDI `LoadImage` 全尺寸可加载 |
| `GetIcon` | 四个动词反射调用均 `S_OK` + 存在的绝对路径（PowerShell 7 下实测） |
| 单元测试 | `tests/EverythingLabeled.ExplorerHost.Tests` 100 项全绿（含新增 `ExplorerIconsTests` 11 项） |
| 壳层 | HKCU `{86ca1aa0-…}` 覆盖：**保持移除**（不要加回，加回则 packed 项全丢） |

## 7. 遗留

- [ ] **肉眼验收**：鼠标右键文件/文件夹，确认四项图标（青绿字形）显示正确 —— 自动化截图在本机受"新菜单是 XAML island、`GetWindowRect` 返回 0,0,0,0 / Shift+F10 走经典菜单"两条限制，最终以人眼为准
- [ ] **Host 标题栏 `find` 字形**：Host 用 `E8B7`（画出来是纸/文档），与菜单的放大镜 `E721` 不一致；要不要一起改成 `E721` 由用户定（一行改动 + `MainWindow.xaml.cs`）
- [ ] `el.ico` 六帧是 PNG 帧，`Icon.ToBitmap()` 这类 GDI+ 消费端会解出乱码（GDI/托盘路径实测正常）。本次未动；若要统一，可用本脚本同款 DIB 帧重生成
- [ ] 经典右键菜单与 packed 项不可兼得的老取舍（[#110 存档](./2026-10-16-explorer-menu-missing.md)）不受本次影响
