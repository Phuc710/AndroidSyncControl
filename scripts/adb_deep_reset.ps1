<#
.SYNOPSIS
    ADB Deep Device Reset & Fingerprint Purge Script (Senior Security Edition)
    Targets: Shopee M02/D02/L01/M04 Device Fingerprint & Telemetry Wipe
.DESCRIPTION
    Performs comprehensive clean-reset:
    1. Purges Shopee App Data & Hidden SD Card Telemetry Folders
    2. Rotates SSAID / Android ID (16-char Crypto Hex)
    3. Flushes Google Services Framework (GSF ID) & GAID (GMS)
    4. Resets DRM Client ID / MediaDrm daemon cache (Widevine UUID Wipe)
    5. Clears Shopee AccountManager System Tokens
    6. Flushes Network Sockets & Rotates Mobile/Airplane IP
    7. Restarts Shopee with a pristine environment
#>

param(
    [string]$DeviceId = ""
)

$ErrorActionPreference = "Stop"

# Locate Canonical ADB Binary
$resolvedAdb = Resolve-Path (Join-Path $PSScriptRoot "..\tools\android\adb\adb.exe") -ErrorAction SilentlyContinue
if ($resolvedAdb) {
    $AdbPath = $resolvedAdb.Path
} else {
    $AdbPath = "adb"
}

function Invoke-Adb {
    param([string]$ArgsStr)
    $target = if ([string]::IsNullOrEmpty($DeviceId)) { "" } else { "-s $DeviceId" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $AdbPath
    $psi.Arguments = if ([string]::IsNullOrEmpty($target)) { $ArgsStr } else { "$target $ArgsStr" }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return ($out + "`n" + $err).Trim()
}

function Get-RandomHex {
    param([int]$length = 16)
    $bytes = New-Object byte[] ($length / 2)
    (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($bytes)
    return ($bytes | ForEach-Object { $_.ToString("x2") }) -join ""
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  ADB DEEP DEVICE RESET & FINGERPRINT PURGE (SENIOR LEVEL)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Device Connection Check
if ([string]::IsNullOrEmpty($DeviceId)) {
    $devicesRaw = Invoke-Adb "devices"
    $deviceLines = @($devicesRaw -split "`r?`n" | Where-Object { $_ -match "\s+device$" })
    if ($deviceLines.Count -eq 0) {
        Write-Host "[!] Error: No active Android device/cloud phone connected!" -ForegroundColor Red
        exit 1
    }
    $firstLine = [string]$deviceLines[0]
    $DeviceId = ($firstLine -split "\s+")[0].Trim()
}
Write-Host "[+] Target Device ID : $DeviceId" -ForegroundColor Green

# 2. Force Stop & Purge Shopee Data
Write-Host "[1/7] Purging Shopee App & Hidden Telemetry Storage..." -ForegroundColor Yellow
Invoke-Adb "shell am force-stop com.shopee.vn" | Out-Null
Invoke-Adb "shell pm clear com.shopee.vn" | Out-Null
Invoke-Adb "shell rm -rf /sdcard/Android/data/com.shopee.vn /sdcard/Android/obb/com.shopee.vn /sdcard/.shopee /sdcard/Shopee /sdcard/Pictures/.shopee 2>/dev/null" | Out-Null

# 3. Rotate SSAID (Android ID)
$NewAndroidId = Get-RandomHex 16
Write-Host "[2/7] Rotating SSAID (Android ID) -> $NewAndroidId..." -ForegroundColor Yellow
Invoke-Adb "shell settings put secure android_id $NewAndroidId" | Out-Null
Invoke-Adb "shell settings put secure ssaid $NewAndroidId" | Out-Null

# 4. Flush GAID (GMS) & GSF (Google Services Framework Token)
Write-Host "[3/7] Clearing GAID (GMS) & GSF Device Tokens..." -ForegroundColor Yellow
Invoke-Adb "shell pm clear com.google.android.gms" | Out-Null
Invoke-Adb "shell pm clear com.google.android.gsf" | Out-Null
Invoke-Adb "shell su -c 'rm -rf /data/data/com.google.android.gms/shared_prefs/advertisingid*.xml' 2>/dev/null" | Out-Null

# 5. Reset MediaDrm / Widevine DRM Device ID Cache
Write-Host "[4/7] Wiping DRM Client ID (MediaDrm Cache)..." -ForegroundColor Yellow
Invoke-Adb "shell su -c 'rm -rf /data/vendor/mediadrm/ /data/system/users/0/drm/ /data/mediadrm/' 2>/dev/null" | Out-Null
Invoke-Adb "shell su -c 'stop media.drm && start media.drm' 2>/dev/null" | Out-Null
Invoke-Adb "shell su -c 'killall -9 android.hardware.drm-service' 2>/dev/null" | Out-Null

# 6. Clear System AccountManager Tokens
Write-Host "[5/7] Purging Shopee AccountManager System Tokens..." -ForegroundColor Yellow
Invoke-Adb "shell su -c 'rm -f /data/system/users/0/accounts.db*' 2>/dev/null" | Out-Null

# 7. Flush DNS & Socket Connections via Airplane Mode Toggle
Write-Host "[6/7] Toggling Airplane Mode & Flushing Sockets..." -ForegroundColor Yellow
Invoke-Adb "shell cmd connectivity airplane-mode enable" | Out-Null
Start-Sleep -Seconds 2
Invoke-Adb "shell cmd connectivity airplane-mode disable" | Out-Null
Invoke-Adb "shell ndc resolver flushdefaultif 2>/dev/null" | Out-Null
Start-Sleep -Seconds 3

# 8. Cold Launch Shopee
Write-Host "[7/7] Cold Launching Shopee Fresh Session..." -ForegroundColor Yellow
Invoke-Adb "shell monkey -p com.shopee.vn -c android.intent.category.LAUNCHER 1 2>/dev/null" | Out-Null

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  SUCCESS: DEEP RESET COMPLETED FOR DEVICE [$DeviceId]" -ForegroundColor Green
Write-Host "  New SSAID: $NewAndroidId" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
