$src = "c:\Users\Phucx\Desktop\ROOT_Shopee\src\AndroidSyncControl\bin\x64\Release\net8.0-windows\win-x64"
$dst = "C:\Program Files\AndroidSyncControl"

Write-Output "[1/3] Terminating any stale processes..."
taskkill /f /im AndroidSyncControl.exe 2>$null | Out-Null
taskkill /f /im scrcpy.exe 2>$null | Out-Null
Start-Sleep -Milliseconds 300

if (Test-Path $dst) {
    Write-Output "[2/3] Syncing latest build to $dst..."
    Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output "      -> Sync to Program Files completed."
}

Write-Output "[3/3] Updating Desktop shortcut to point to latest workspace launcher..."
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktop "AndroidSyncControl.lnk"
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
$sc.TargetPath = "c:\Users\Phucx\Desktop\ROOT_Shopee\run.bat"
$sc.WorkingDirectory = "c:\Users\Phucx\Desktop\ROOT_Shopee"
$sc.IconLocation = "$src\appicon.ico"
if (-not (Test-Path "$src\appicon.ico")) {
    $sc.IconLocation = "$src\AndroidSyncControl.exe,0"
}
$sc.Description = "AndroidSyncControl - Auto Sync and Launch"
$sc.Save()
Write-Output "      -> Desktop shortcut updated to point to run.bat!"
Write-Output "[+] All synced! Code toi dau chay toi do, khong con dinh ban cu."
