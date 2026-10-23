#Requires -Version 5.1
<#
.SYNOPSIS
    生成 EverythingLabeled.Service 的 exe 图标（service.ico）。

.DESCRIPTION
    视觉：Segoe MDL2「服务器」E968 + 右下角「标签」E8EC 角标，主色 #2EC4B6，
    与 Tray 纯标签 el.ico、Host 文件夹+标签 host.ico 区分，表示本机标签守护进程。

    帧编码与 Explorer 菜单图标相同：多尺寸 DIB（32bpp），不用 PNG 帧。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-service-icon.ps1
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
$assetDir = Join-Path $root 'src\EverythingLabeled.Service\Assets'
$iconPath = Join-Path $assetDir 'service.ico'
$sizes = @(16, 20, 24, 32, 48, 256)

if (-not (Test-Path -LiteralPath $assetDir)) {
    New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
}

$ink = [System.Drawing.ColorTranslator]::FromHtml($Color)
if ($ink.A -ne 255) { $ink = [System.Drawing.Color]::FromArgb(255, $ink.R, $ink.G, $ink.B) }

Write-Host "生成 Service 图标（$Color，尺寸 $($sizes -join '/')）..."
$frames = @()
foreach ($size in $sizes) {
    $frames += New-ServiceIconBitmap -Size $size -Ink $ink
}

try {
    Write-IcoFile -Path $iconPath -Frames $frames
    Write-Host ("      service.ico  ({0} 帧, {1} 字节)" -f $frames.Count, (Get-Item -LiteralPath $iconPath).Length)
} finally {
    $frames | ForEach-Object { $_.Dispose() }
}

if ($PreviewDir) {
    if (-not (Test-Path -LiteralPath $PreviewDir)) { New-Item -ItemType Directory -Path $PreviewDir -Force | Out-Null }
    $ico = New-Object System.Drawing.Icon($iconPath)
    $bitmap = $ico.ToBitmap()
    try {
        $bitmap.Save((Join-Path $PreviewDir 'service-48.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "      预览已导出：$PreviewDir"
    } finally {
        $bitmap.Dispose(); $ico.Dispose()
    }
}

Write-Host ''
Write-Host "完成。图标：$iconPath"
