$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir

Write-Host "========================================================" -ForegroundColor Magenta
Write-Host "   ANDROIDSYNCCONTROL - SETUP & ENVIRONMENT CHECK" -ForegroundColor Magenta
Write-Host "========================================================" -ForegroundColor Magenta

# 1. Check ADB
$adbPath = "tools\android\adb\adb.exe"
if (Test-Path $adbPath) {
    $v = & $adbPath version | Select-Object -First 1
    Write-Host "[OK] ADB: $v" -ForegroundColor Green
} else {
    Write-Host "[ERR] Missing $adbPath" -ForegroundColor Red
}

# 2. Check scrcpy
$scrcpyPath = "tools\android\scrcpy\scrcpy.exe"
if (Test-Path $scrcpyPath) {
    $v = & $scrcpyPath --version | Select-Object -First 1
    Write-Host "[OK] Scrcpy: $v" -ForegroundColor Green
} else {
    Write-Host "[ERR] Missing $scrcpyPath" -ForegroundColor Red
}

Write-Host "`nEnvironment ready." -ForegroundColor Cyan
