#Requires -Version 5.1
[CmdletBinding()]
param()

# Match EverythingLabeled.Cli console encoding when launched from build-ext.bat / cmd.
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
[Console]::InputEncoding = $utf8
$OutputEncoding = $utf8

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Ext = Join-Path $Root 'ext'
$PackageJson = Join-Path $Ext 'package.json'

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Write-Error '未找到 npm。请安装 Node.js ≥ 20 并确保 PATH 可用。'
}

if (-not (Test-Path -LiteralPath $PackageJson)) {
    Write-Error "未找到扩展工程：$PackageJson"
}

Push-Location -LiteralPath $Ext
try {
    if (-not (Test-Path -LiteralPath (Join-Path $Ext 'node_modules'))) {
        Write-Host 'Installing ext dependencies (npm ci)...'
        npm ci
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    Write-Host 'Building browser extension (ext/)...'
    npm run build
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
