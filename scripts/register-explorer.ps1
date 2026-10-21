#Requires -Version 5.1
<#
.SYNOPSIS
    构建并注册 EverythingLabeled 的 Explorer 稀疏包（EverythingLabeled.ExplorerDev）。

.DESCRIPTION
    对应 docs/explorer.md 的「注册（稀疏包 + ExternalLocation）」。
    本脚本兜住两个踩过的坑：

      1. 打包读的是 bin 输出目录里的 AppxManifest.xml，而它是**构建产物**
         （csproj 用 CopyToOutputDirectory=PreserveNewest 从 Package\ 拷出）。
         源码回滚/改过但没重新 build 时，会把上一次的旧清单打进包 —— 注册"成功"，
         右键菜单却还是旧内容（甚至指向已不存在的 DLL）。脚本会比对并强制刷成源码版。
      2. Add-AppxPackage 不允许降级。已装版本 >= 清单版本会报 0x80073D06。
         脚本注册前先比版本，低了就明确报错。

    注册成功后若右键菜单没出现，重启 explorer.exe 或注销重登。

.PARAMETER Configuration
    构建配置，默认 Debug。输出目录 ...\bin\<Configuration>\net8.0-windows。

.PARAMETER Thumbprint
    代码签名证书指纹。省略则按 Subject CN=EverythingLabeled-Dev 从 CurrentUser\My 里选。

.PARAMETER SkipBuild
    跳过 dotnet build（仅当你确定输出目录已是最新时才用）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\register-explorer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$Thumbprint,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$identityName = 'EverythingLabeled.ExplorerDev'
$certSubject = 'CN=EverythingLabeled-Dev'   # 清单 Publisher / 证书 Subject
$certName = 'EverythingLabeled-Dev'         # signtool /n 只认裸 CN，不要带 CN= 前缀

$root = Split-Path -Parent $PSScriptRoot
$projDir = Join-Path $root 'src\EverythingLabeled.ExplorerCommand'
$proj = Join-Path $projDir 'EverythingLabeled.ExplorerCommand.csproj'
$srcManifest = Join-Path $projDir 'Package\AppxManifest.xml'
$out = Join-Path $projDir "bin\$Configuration\net8.0-windows"
$outManifest = Join-Path $out 'AppxManifest.xml'

function Test-SameFileContent([string]$A, [string]$B) {
    if (-not (Test-Path -LiteralPath $A) -or -not (Test-Path -LiteralPath $B)) { return $false }
    return [System.IO.File]::ReadAllText($A) -eq [System.IO.File]::ReadAllText($B)
}

function Get-Manifest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "找不到清单：$Path" }
    return [xml](Get-Content -LiteralPath $Path -Raw -Encoding UTF8)
}

function Get-ComServerExtension($doc) {
    $exts = @($doc.Package.Applications.Application.Extensions.Extension)
    $com = @($exts | Where-Object { $_.Category -eq 'windows.comServer' })
    if ($com.Count -ne 1) { throw "清单里 windows.comServer 扩展应有 1 个，实际 $($com.Count) 个。" }
    return $com[0]
}

# ---- 1. 构建 ----------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host '[1/5] dotnet build ...'
    & dotnet build $proj -c $Configuration -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（exit $LASTEXITCODE）。" }
} else {
    Write-Host '[1/5] 跳过构建（-SkipBuild）。'
}

# ---- 2. 校验输出目录：清单新鲜 + 声明的文件都在 ------------------------------
Write-Host '[2/5] 校验输出目录 ...'
if (-not (Test-SameFileContent $srcManifest $outManifest)) {
    Write-Warning "bin 里的 AppxManifest.xml 不是源码版（构建产物过期），已用 Package\AppxManifest.xml 覆盖：$outManifest"
    Copy-Item -LiteralPath $srcManifest -Destination $outManifest -Force
}

$doc = Get-Manifest $outManifest
$version = [version]$doc.Package.Identity.Version
$arch = $doc.Package.Identity.ProcessorArchitecture
Write-Host "      清单：Version=$version  Arch=$arch  Identity=$($doc.Package.Identity.Name)"
if ($doc.Package.Identity.Name -ne $identityName) { throw "Identity.Name 应为 $identityName。" }

# Application Executable 必须存在，否则包起来了也跑不了
foreach ($app in @($doc.Package.Applications.Application)) {
    $exe = Join-Path $out $app.Executable
    if (-not (Test-Path -LiteralPath $exe)) { throw "清单声明 Executable=$($app.Executable)，但输出目录里没有：$exe" }
}

# com:Class 里 DllPath 指向的 DLL 必须存在 —— 少一个就该 CLSID 点不动
$com = Get-ComServerExtension $doc
foreach ($class in @($com.ComServer.SurrogateServer.Class)) {
    $dll = Join-Path $out $class.Path
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "清单声明 com:Class $($class.Id) 的 Path=$($class.Path)，但输出目录里没有：$dll"
    }
}
$clsidCount = @($com.ComServer.SurrogateServer.Class).Count

# 文件在还不够：comhost 若没嵌进新 CLSID，Explorer 创建失败会静默丢那一项
if (-not ('ElComProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ElComProbe {
  [DllImport("ole32.dll")]
  public static extern int CoCreateInstance(ref Guid clsid, IntPtr unk, uint ctx, ref Guid iid, out IntPtr ppv);
}
'@
}
$iidUnknown = [guid]'00000000-0000-0000-C000-000000000046'
$clsctxAll = 23
foreach ($class in @($com.ComServer.SurrogateServer.Class)) {
    $clsid = [guid]$class.Id
    $iid = $iidUnknown
    $ppv = [IntPtr]::Zero
    $hr = [ElComProbe]::CoCreateInstance([ref]$clsid, [IntPtr]::Zero, $clsctxAll, [ref]$iid, [ref]$ppv)
    if ($ppv -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::Release($ppv) | Out-Null
    }
    if ($hr -ne 0) {
        throw ("清单 CLSID {0} CoCreate 失败 hr=0x{1:X8}（0x80040111=comhost 未嵌入该类，常见于增量构建跳过拷贝）。删掉输出目录的 *.comhost.dll 后重建，或直接跑本脚本（会先 build）。" -f $class.Id, ($hr -band 0xFFFFFFFF))
    }
}
Write-Host "      CoCreate $clsidCount 个 CLSID 均成功。"

$verbCount = 0
foreach ($type in @($doc.Package.Applications.Application.Extensions.Extension | Where-Object { $_.Category -eq 'windows.fileExplorerContextMenus' }).FileExplorerContextMenus.ItemType) {
    $verbCount += @($type.Verb).Count
}
Write-Host "      com:Class=$clsidCount 个，右键 Verb=$verbCount 个。"

# ---- 3. 版本比较：Add-AppxPackage 不允许降级 --------------------------------
$installed = Get-AppxPackage -Name $identityName
if ($installed) {
    Write-Host "[3/5] 已安装：$($installed.PackageFullName)"
    if ($version -lt $installed.Version) {
        throw @"
清单版本 $version 低于已安装的 $($installed.Version)，Add-AppxPackage 会报 0x80073D06。
二选一：
  a) 把 $srcManifest 的 Version 提到 $($installed.Version) 以上；或
  b) 先卸载：Get-AppxPackage -Name $identityName | Remove-AppxPackage
"@
    }
    if ($version -eq $installed.Version) {
        Write-Warning "版本号与已安装的相同（$version），会走重装/修复路径。若发现改动没生效，请提版本号或先 Remove-AppxPackage。"
    }
} else {
    Write-Host '[3/5] 尚未安装，首次注册。'
}

# ---- 4. 打包 + 签名 ---------------------------------------------------------
Write-Host '[4/5] makeappx pack + signtool sign ...'
$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$kit = Get-ChildItem -LiteralPath $kitsRoot -Directory |
    Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
    Sort-Object { [version]$_.Name } |
    Select-Object -Last 1
if (-not $kit) { throw "找不到 Windows Kits 10 的 bin\<ver>\x64。" }
$makeappx = Join-Path $kit.FullName 'x64\makeappx.exe'
$signtool = Join-Path $kit.FullName 'x64\signtool.exe'
Write-Host "      使用 $($kit.Name) 的工具。"

$stage = Join-Path $env:TEMP 'el-explorer-sparse'
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Join-Path $stage 'Assets') -Force | Out-Null
Copy-Item -LiteralPath $outManifest -Destination (Join-Path $stage 'AppxManifest.xml')
Copy-Item -Path (Join-Path $out 'Assets\*') -Destination (Join-Path $stage 'Assets') -Recurse

$msix = Join-Path $env:TEMP "$identityName.msix"
Remove-Item -LiteralPath $msix -Force -ErrorAction SilentlyContinue
& $makeappx pack /d $stage /p $msix /nv
if ($LASTEXITCODE -ne 0) { throw "makeappx pack 失败（exit $LASTEXITCODE）。" }

# 有 -Thumbprint 用指纹，否则按裸 CN 从 CurrentUser\My 里挑（/n 不接受 "CN=" 前缀）
if ($Thumbprint) {
    & $signtool sign /fd SHA256 /sha1 $Thumbprint $msix
} else {
    & $signtool sign /fd SHA256 /n $certName $msix
}
if ($LASTEXITCODE -ne 0) {
    throw @"
signtool 签名失败（exit $LASTEXITCODE）。先确认证书还在：
  certutil -store -user My | Select-String '$certName'
缺失时按 docs/explorer.md「侧载前提」重建并信任（证书 Subject 必须是 $certSubject）。
"@
}

# ---- 5. 注册 + 回读校验 -----------------------------------------------------
Write-Host '[5/5] Add-AppxPackage -ExternalLocation ...'
try {
    Add-AppxPackage -Path $msix -ExternalLocation $out
} catch {
    throw "注册失败：$($_.Exception.Message)`n（0x80073D06 = 版本降级被拒，见上面 [3/5]。）"
}

$pkg = Get-AppxPackage -Name $identityName
if (-not $pkg) { throw '注册后 Get-AppxPackage 仍查不到该包。' }
Write-Host "      已注册：$($pkg.PackageFullName)"
Write-Host "      ExternalLocation：$out"

# 回读 PackagedCom：确认每个 CLSID 实际解析到的 DllPath 真实存在（这次踩坑的地方）
$pkgKey = "HKLM:\SOFTWARE\Classes\PackagedCom\Package\$($pkg.PackageFullName)"
if (Test-Path $pkgKey) {
    Write-Host '      PackagedCom 解析：'
    Get-ChildItem -Path (Join-Path $pkgKey 'Class') | Sort-Object PSChildName | ForEach-Object {
        $dll = (Get-ItemProperty -LiteralPath $_.PSPath).DllPath
        $ok = if (Test-Path -LiteralPath (Join-Path $out $dll)) { 'OK' } else { '缺失！' }
        Write-Host ("        {0}  ->  {1}  [{2}]" -f $_.PSChildName, $dll, $ok)
    }
} else {
    Write-Warning "HKLM 里找不到 $pkgKey —— COM 服务器没注册上，菜单不会出现。"
}

Write-Host ''
Write-Host '完成。菜单没出现时：Stop-Process -Name explorer -Force（会自动重启），或注销重登。'
