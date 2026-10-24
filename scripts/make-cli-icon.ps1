#Requires -Version 5.1
<#
.SYNOPSIS
    生成 EverythingLabeled.Cli 的 exe 图标（cli.ico → el.exe）。

.DESCRIPTION
    视觉：Segoe MDL2「命令提示符」E756 + 右下角「标签」E8EC 角标，主色 #2EC4B6。
    与 Tray 纯标签 el.ico、Host 文件夹+标签、Service 服务器+标签区分，表示 CLI 薄客户端。

    帧编码与 Explorer 菜单图标相同：多尺寸 DIB（32bpp），不用 PNG 帧。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-cli-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Color = '#2EC4B6',
    [string]$PreviewDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

. (Join-Path $PSScriptRoot 'IcoDibHelpers.ps1')

$root = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $root 'src\EverythingLabeled.Cli\Assets'
$iconPath = Join-Path $assetDir 'cli.ico'
$sizes = @(16, 20, 24, 32, 48, 256)

if (-not (Test-Path -LiteralPath $assetDir)) {
    New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
}

$ink = [System.Drawing.ColorTranslator]::FromHtml($Color)
if ($ink.A -ne 255) { $ink = [System.Drawing.Color]::FromArgb(255, $ink.R, $ink.G, $ink.B) }

Write-Host "生成 CLI 图标（$Color，尺寸 $($sizes -join '/')）..."
$frames = @()
foreach ($size in $sizes) {
    $frames += New-CliIconBitmap -Size $size -Ink $ink
}

try {
    Write-IcoFile -Path $iconPath -Frames $frames
    Write-Host ("      cli.ico  ({0} 帧, {1} 字节)" -f $frames.Count, (Get-Item -LiteralPath $iconPath).Length)
} finally {
    $frames | ForEach-Object { $_.Dispose() }
}

if ($PreviewDir) {
    if (-not (Test-Path -LiteralPath $PreviewDir)) { New-Item -ItemType Directory -Path $PreviewDir -Force | Out-Null }
    foreach ($size in @(16, 32, 48, 256)) {
        $bmp = New-CliIconBitmap -Size $size -Ink $ink
        try {
            $bmp.Save((Join-Path $PreviewDir ("cli-{0}.png" -f $size)), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $bmp.Dispose()
        }
    }
    Write-Host "      预览已导出：$PreviewDir"
}

Write-Host ''
Write-Host "完成。图标：$iconPath"
