<#
.SYNOPSIS
    AndroidSyncControl — Production 1-Click Publish Pipeline (Zero Cloud Dependencies)
    Flow:
      1. Run release.ps1 (Validate -> Clean -> Build -> Test -> Publish -> Stage -> Smoke Test -> Package -> Hash -> Manifest -> NSIS)
      2. Locate tools/github/gh.exe
      3. Commit & push code to origin main
      4. Automatically create GitHub Release and upload 4 artifacts (ZIP, Setup.exe, SHA-256, update-manifest.json)
.EXAMPLE
    .\scripts\build\publish.ps1 -Version 1.0.1
#>
param(
    [string]$Version,
    [string]$Channel = "stable",
    [switch]$SkipTests,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir

# 1. Read Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $rootDir "VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile -Raw).Trim()
    } else {
        $Version = "1.0.0"
    }
}

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " AndroidSyncControl 1-Click Automated Publisher v$Version" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 2. Run release.ps1
$releaseScript = Join-Path $rootDir "scripts\build\release.ps1"
$releaseParams = @{
    Version = $Version
    Channel = $Channel
}
if ($SkipTests) { $releaseParams["SkipTests"] = $true }
if ($SkipSmokeTest) { $releaseParams["SkipSmokeTest"] = $true }

& $releaseScript @releaseParams

# 3. Locate gh.exe
$ghExe = $null
$localGh = Join-Path $rootDir "tools\github\gh.exe"
if (Test-Path $localGh) {
    $ghExe = $localGh
} elseif (Get-Command "gh" -ErrorAction SilentlyContinue) {
    $ghExe = "gh"
}

if (-not $ghExe) {
    Write-Error "GitHub CLI (gh.exe) not found in tools/github/ or system PATH."
}

# 4. Check gh auth status
$authStatus = & $ghExe auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Yellow
    Write-Host " [YÊU CẦU ĐĂNG NHẬP 1 LẦN DUY NHẤT VÀO GITHUB CLI]" -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Yellow
    Write-Host "Mở terminal và gõ lệnh sau để đăng nhập:" -ForegroundColor Cyan
    Write-Host "  .\tools\github\gh.exe auth login" -ForegroundColor White
    Write-Host "Chọn: GitHub.com -> HTTPS -> Login with a web browser" -ForegroundColor DarkGray
    Write-Host "Sau khi đăng nhập, bạn có thể chạy lại lệnh publish này." -ForegroundColor DarkGray
    Write-Host "========================================================" -ForegroundColor Yellow
    exit 1
}

# 5. Git Commit & Push
Write-Host "Pushing code to origin main..." -ForegroundColor Yellow
git add .
git commit -m "release: v$Version" 2>$null
git push origin main

# 6. Publish via gh release create
$releaseDir = Join-Path $rootDir "release"
$tag = "v$Version"
$title = "AndroidSyncControl $tag"

Write-Host "Publishing Release $tag to GitHub..." -ForegroundColor Yellow

$zipFile = Get-ChildItem -Path $releaseDir -Filter "*$Version*.zip" | Select-Object -First 1 -ExpandProperty FullName
$exeFile = Get-ChildItem -Path $releaseDir -Filter "*$Version*-Setup.exe" | Select-Object -First 1 -ExpandProperty FullName
$shaFile = Get-ChildItem -Path $releaseDir -Filter "*$Version*.sha256" | Select-Object -First 1 -ExpandProperty FullName
$manifestFile = Join-Path $releaseDir "update-manifest.json"

$filesToUpload = @($zipFile, $exeFile, $shaFile, $manifestFile) | Where-Object { $_ -and (Test-Path $_) }

& $ghExe release create $tag $filesToUpload --title $title --generate-notes

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host " BẢN RELEASE $tag ĐÃ ĐƯỢC TỰ ĐỘNG PHÁT HÀNH LÊN GITHUB!" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host "Xem tại: https://github.com/Phuc710/AndroidSyncControl/releases/tag/$tag" -ForegroundColor Cyan
} else {
    Write-Error "Lỗi khi upload release qua GitHub CLI."
}
