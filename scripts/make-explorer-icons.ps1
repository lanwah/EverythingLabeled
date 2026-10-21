#Requires -Version 5.1
<#
.SYNOPSIS
    生成 Explorer 右键菜单四项的图标，以及稀疏包里三个 Logo。

.DESCRIPTION
    Explorer 四项（打标 / 去标 / 看标签 / 按标签查找）以前是黑方块，两层原因：
    `ExplorerCommandBase.GetIcon` 返回 E_NOTIMPL，而 `Package\Assets\` 三个 Logo
    是纯黑占位图 —— 命令不给图标时壳层回落到包 Logo。

    本脚本产出：

      1. `Package\Assets\ElTag.ico` / `ElUntag.ico` / `ElTags.ico` / `ElFind.ico`
         —— 四个动词图标，字形沿用 Host 标题栏同一套 Segoe MDL2 映射
         （`ExplorerHost\MainWindow.xaml.cs` 的走位：tag=E8EC / untag=E74D /
         tags=E8FD / find=E8B7），青绿 #2EC4B6 画在透明底上，多尺寸 16/20/24/32/48。
      2. `Package\Assets\{Square44x44Logo,Square150x150Logo,StoreLogo}.png`
         —— 从 `ExplorerShared\Assets\el.ico` 的 256 帧缩放，替掉黑占位。

    踩过的坑：

      - **帧编码用 DIB（32bpp BMP），不要用 PNG 帧**。起初照 `el.ico` 的写法出 PNG 帧，
        实测 `System.Drawing.Icon.ToBitmap()` 对这些帧**解码成乱码或直接抛异常**
        （`el.ico` 16px 帧本应透明的一角读出来是 `A33 R88 G177`；48/256 帧抛
        `MethodInvocationException`）。PNG-in-ICO 只有 256 尺寸是官方推荐用法，
        ≤48 的传统 DIB 帧才是各消费端（GDI / GDI+ / Shell）都稳的编码。
      - `System.Drawing.Icon` 的 `new Icon(path, 256, 256)` 取不到 PNG 大帧
        （只会给到 48），所以读 `el.ico` 的 256 帧要自己解 ICO 目录取原始 PNG 字节，
        别用 `Icon` 类。
      - 本机从 Git Bash 调 `powershell -NoProfile` 时 **没有 `Cert:` 盘、
        没有 `Get-FileHash`**（见 plans\2026-10-16-explorer-menu-missing.md §2.2）。
        本脚本只用 `System.IO` / `System.Drawing`，不碰这两样。
      - 小尺寸清晰度靠 4× 超采样再降采样，直接按 16px 画会糊。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-explorer-icons.ps1
#>
[CmdletBinding()]
param(
    # 图标主色（青绿，与 el.ico / Host 主题 HostTealColor 同值）
    [string]$Color = '#2EC4B6',

    # 每个尺寸都额外导一张 PNG 到该目录，便于肉眼验收（留空则不导）
    [string]$PreviewDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

. (Join-Path $PSScriptRoot 'IcoDibHelpers.ps1')

$root = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $root 'src\EverythingLabeled.ExplorerCommand\Package\Assets'
$sourceIcon = Join-Path $root 'src\EverythingLabeled.ExplorerShared\Assets\el.ico'

# 动词图标：文件名 -> Segoe MDL2 码位。
# 前三个与 ExplorerHost\MainWindow.xaml.cs 的标题栏走位同源（tag=E8EC / untag=E74D /
# tags=E8FD）；find 这里用 E721（放大镜）—— Host 标题栏用的是 E8B7，但那个字形画出来是
# 一页纸/文档，当"查找"不对，放大镜才是正解，也是设计稿搜索框用的字形。
$verbs = [ordered]@{
    'ElTag.ico'     = 0xE8EC   # 打标
    'ElUntag.ico'   = 0xE74D   # 去标
    'ElTags.ico'    = 0xE8FD   # 看标签
    'ElLibrary.ico'   = 0xE8F1   # 管理标签（词库）
    'ElResources.ico' = 0xE8A5   # 管理资源
    'ElFind.ico'      = 0xE721   # 按标签查找（放大镜）
}

$sizes = @(16, 20, 24, 32, 48)
$glyphScale = 0.95   # 字形占画布比例；MDL2 字形自带留白，0.95 视觉居中偏满

if (-not (Test-Path -LiteralPath $sourceIcon)) { throw "找不到源图标：$sourceIcon" }
if (-not (Test-Path -LiteralPath $assetDir)) { New-Item -ItemType Directory -Path $assetDir -Force | Out-Null }

$ink = [System.Drawing.ColorTranslator]::FromHtml($Color)
if ($ink.A -ne 255) { $ink = [System.Drawing.Color]::FromArgb(255, $ink.R, $ink.G, $ink.B) }

# ---- 1. 四个动词图标 --------------------------------------------------------
Write-Host "[1/2] 生成动词图标（$Color，尺寸 $($sizes -join '/')）..."
foreach ($name in $verbs.Keys) {
    $frames = @()
    foreach ($size in $sizes) { $frames += New-GlyphBitmap -CodePoint $verbs[$name] -Size $size -Ink $ink -GlyphScale $glyphScale }
    try {
        $target = Join-Path $assetDir $name
        Write-IcoFile -Path $target -Frames $frames
        Write-Host ("      {0}  ({1} 帧, {2} 字节)" -f $name, $frames.Count, (Get-Item -LiteralPath $target).Length)
    } finally { $frames | ForEach-Object { $_.Dispose() } }
}

# ---- 2. 包 Logo（从 el.ico 缩放，替掉黑占位）--------------------------------
Write-Host '[2/2] 生成包 Logo（源：ExplorerShared/Assets/el.ico）...'
$sourcePng = Get-IcoFramePng -Path $sourceIcon -Size 256
$stream = New-Object System.IO.MemoryStream($sourcePng, $false)
$source = [System.Drawing.Image]::FromStream($stream)
try {
    $logos = [ordered]@{
        'Square44x44Logo.png'   = 44
        'Square150x150Logo.png' = 150
        'StoreLogo.png'         = 50
    }
    foreach ($name in $logos.Keys) {
        $target = Join-Path $assetDir $name
        Write-ScaledPng -Source $source -Path $target -Size $logos[$name]
        Write-Host ("      {0}  ({1}x{1})" -f $name, $logos[$name])
    }
} finally { $source.Dispose(); $stream.Dispose() }

# ---- 可选的肉眼验收导出 -----------------------------------------------------
if ($PreviewDir) {
    if (-not (Test-Path -LiteralPath $PreviewDir)) { New-Item -ItemType Directory -Path $PreviewDir -Force | Out-Null }
    foreach ($name in $verbs.Keys) {
        $ico = New-Object System.Drawing.Icon((Join-Path $assetDir $name))
        $bitmap = $ico.ToBitmap()
        try { $bitmap.Save((Join-Path $PreviewDir ([IO.Path]::GetFileNameWithoutExtension($name) + '-32.png')), [System.Drawing.Imaging.ImageFormat]::Png) }
        finally { $bitmap.Dispose(); $ico.Dispose() }
    }
    Write-Host "      预览已导出：$PreviewDir"
}

Write-Host ''
Write-Host "完成。图标：$assetDir"
Write-Host '改动生效需重新注册稀疏包（版本号已递增才会被 Add-AppxPackage 接受）：'
Write-Host '    powershell -ExecutionPolicy Bypass -File scripts\register-explorer.ps1'
