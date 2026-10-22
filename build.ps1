#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

# Match EverythingLabeled.Cli console encoding when launched from build.bat / cmd.
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$OutputEncoding = $utf8

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Sln = Join-Path $Root 'EverythingLabeled.sln'

function Stop-RepoBuildLockingProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    # Find 关窗只 Hide；托盘「退出」也不结束 Host。两者都会占着输出目录里的 DLL。
    $names = @('EverythingLabeled.Tray', 'EverythingLabeled.ExplorerHost')
    $rootFull = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar

    $running = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        return
    }

    $toStop = @()
    foreach ($proc in $running) {
        $exe = $null
        try {
            $exe = $proc.MainModule.FileName
        }
        catch {
            $exe = $null
        }

        if ([string]::IsNullOrWhiteSpace($exe)) {
            Write-Host "跳过无法读取路径的进程：$($proc.ProcessName) ($($proc.Id))"
            continue
        }

        $exeFull = [System.IO.Path]::GetFullPath($exe)
        if (-not $exeFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "保留仓库外进程：$($proc.ProcessName) ($($proc.Id)) $exeFull"
            continue
        }

        Write-Host "结束占用编译输出的进程：$($proc.ProcessName) ($($proc.Id))"
        $toStop += $proc
    }

    if ($toStop.Count -eq 0) {
        return
    }

    $toStop | Stop-Process -Force
    $stuck = @()
    foreach ($proc in $toStop) {
        try {
            if (-not $proc.WaitForExit(10000)) {
                $stuck += "$($proc.ProcessName) ($($proc.Id))"
            }
        }
        catch {
            # 进程已退出时再等待可能失败，视为已结束。
        }
    }

    if ($stuck.Count -gt 0) {
        Write-Error ("编译前未能结束占用输出目录的进程：{0}" -f ($stuck -join ', '))
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error '未找到 dotnet。请安装 .NET 8 SDK 并确保 PATH 可用。'
}

if (-not (Test-Path -LiteralPath $Sln)) {
    Write-Error "未找到解决方案：$Sln"
}

Stop-RepoBuildLockingProcesses -RepoRoot $Root

Write-Host "Building $Sln ($Configuration)..."
dotnet build $Sln -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $Root 'build-ext.ps1')
exit $LASTEXITCODE
