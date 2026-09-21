$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir
Write-Host "Building AndroidSyncControl ($rootDir)..." -ForegroundColor Cyan
dotnet build AndroidSyncControl.sln -c Release
