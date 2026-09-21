$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir
Write-Host "Running AndroidSyncControl ($rootDir)..." -ForegroundColor Green
dotnet run --project src\AndroidSyncControl\AndroidSyncControl.csproj
