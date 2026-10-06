#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Sln = Join-Path $Root 'EverythingLabeled.sln'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error '未找到 dotnet。请安装 .NET 8 SDK 并确保 PATH 可用。'
}

if (-not (Test-Path -LiteralPath $Sln)) {
    Write-Error "未找到解决方案：$Sln"
}

Write-Host "Building $Sln ($Configuration)..."
dotnet build $Sln -c $Configuration --nologo
exit $LASTEXITCODE
