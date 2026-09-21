<#
.SYNOPSIS
    Runs the automated test suites for AndroidSyncControl.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Running Tests ($Configuration)..." -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

dotnet test AndroidSyncControl.sln -c $Configuration --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed with exit code $LASTEXITCODE"
}

Write-Host "All tests passed successfully!" -ForegroundColor Green
