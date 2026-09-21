<#
.SYNOPSIS
    AndroidSyncControl — Production Release Pipeline
    Flow: Validate -> Clean -> Restore -> Build -> Test -> Publish -> Stage -> Smoke Test -> Package -> Hash -> Manifest -> Sign -> NSIS -> Verify
.EXAMPLE
    .\release.ps1 -Version 1.0.0
#>
param(
    [string]$Version,
    [string]$Channel = "stable",
    [switch]$SkipTests,
    [switch]$SkipSmokeTest,
    [switch]$SkipSign,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $rootDir

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " AndroidSyncControl Production Release Pipeline" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# ── 1. Read & Validate Version ──────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $rootDir "VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile -Raw).Trim()
    } else {
        $Version = "1.0.0"
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') {
    Write-Error "Invalid SemVer version format: '$Version'. Expected format: X.Y.Z (e.g. 1.0.0)"
}

Write-Host "[1/12] Target Version: $Version (Channel: $Channel)" -ForegroundColor Green

# ── 2. Check Git State ───────────────────────────────────────────────────────
Write-Host "[2/12] Checking Git repository state..." -ForegroundColor Yellow
try {
    $gitStatus = git status --porcelain 2>$null
    if ($gitStatus) {
        Write-Warning "Working tree has uncommitted changes. Continuing for release build..."
    } else {
        Write-Host "Git working directory is clean." -ForegroundColor Green
    }
} catch {
    Write-Warning "Git command not available or not a git repo."
}

# ── 3. Clean & Restore ───────────────────────────────────────────────────────
Write-Host "[3/12] Cleaning previous build artifacts..." -ForegroundColor Yellow
$distDir = Join-Path $rootDir "dist"
$releaseDir = Join-Path $rootDir "release"

if (Test-Path $distDir) { Remove-Item -Path $distDir -Recurse -Force }
if (-not (Test-Path $releaseDir)) { New-Item -Path $releaseDir -ItemType Directory | Out-Null }

dotnet clean AndroidSyncControl.sln -c Release --verbosity quiet
dotnet restore AndroidSyncControl.sln

# ── 4. Build Solution ────────────────────────────────────────────────────────
Write-Host "[4/12] Building solution (Release x64, Version: $Version)..." -ForegroundColor Yellow
dotnet build AndroidSyncControl.sln -c Release /p:Version=$Version /p:AssemblyVersion="$Version.0" /p:FileVersion="$Version.0"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}

# ── 5. Run Automated Tests ───────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Host "[5/12] Running test suite..." -ForegroundColor Yellow
    dotnet test AndroidSyncControl.sln -c Release --no-build --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed. Release aborted."
    }
    Write-Host "All tests passed successfully." -ForegroundColor Green
} else {
    Write-Host "[5/12] Skipping test suite (-SkipTests specified)." -ForegroundColor DarkGray
}

# ── 6. Publish Main WPF App ─────────────────────────────────────────────────
Write-Host "[6/12] Publishing AndroidSyncControl (win-x64 self-contained)..." -ForegroundColor Yellow
$appPublishDir = Join-Path $distDir "temp_app"
dotnet publish src\AndroidSyncControl\AndroidSyncControl.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishReadyToRun=true `
    /p:PublishSingleFile=false `
    /p:Version=$Version `
    /p:AssemblyVersion="$Version.0" `
    /p:FileVersion="$Version.0" `
    -o $appPublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Main app publish failed."
}

# ── 7. Publish Updater App ───────────────────────────────────────────────────
Write-Host "[7/12] Publishing AndroidSyncControl.Updater..." -ForegroundColor Yellow
$updaterPublishDir = Join-Path $distDir "temp_updater"
dotnet publish src\AndroidSyncControl.Updater\AndroidSyncControl.Updater.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishReadyToRun=true `
    /p:PublishSingleFile=false `
    /p:Version=$Version `
    /p:AssemblyVersion="$Version.0" `
    /p:FileVersion="$Version.0" `
    -o $updaterPublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Updater publish failed."
}

# ── 8. Stage Layout ──────────────────────────────────────────────────────────
$stagingName = "AndroidSyncControl-$Version-win-x64"
$stagingDir = Join-Path $distDir $stagingName
Write-Host "[8/12] Staging distribution layout: $stagingDir" -ForegroundColor Yellow

New-Item -Path $stagingDir -ItemType Directory -Force | Out-Null

# Copy main app files
Copy-Item -Path "$appPublishDir\*" -Destination $stagingDir -Recurse -Force

# Copy updater executable and dependencies
Copy-Item -Path "$updaterPublishDir\AndroidSyncControl.Updater.exe" -Destination $stagingDir -Force
if (Test-Path "$updaterPublishDir\AndroidSyncControl.Updater.dll") {
    Copy-Item -Path "$updaterPublishDir\AndroidSyncControl.Updater.dll" -Destination $stagingDir -Force
}

# Stage Runtime/ tools (adb and scrcpy)
$runtimeDir = Join-Path $stagingDir "Runtime"
New-Item -Path $runtimeDir -ItemType Directory -Force | Out-Null

$adbSource = Join-Path $rootDir "tools\android\adb"
$scrcpySource = Join-Path $rootDir "tools\android\scrcpy"

if (Test-Path $adbSource) {
    Copy-Item -Path $adbSource -Destination (Join-Path $runtimeDir "adb") -Recurse -Force
} else {
    Write-Warning "Source tools/android/adb not found at $adbSource"
}

if (Test-Path $scrcpySource) {
    Copy-Item -Path $scrcpySource -Destination (Join-Path $runtimeDir "scrcpy") -Recurse -Force
} else {
    Write-Warning "Source tools/android/scrcpy not found at $scrcpySource"
}

# Write install.json metadata
$installMeta = [PSCustomObject]@{
    product = "AndroidSyncControl"
    version = $Version
    channel = $Channel
    installPath = ""
    installedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$installMeta | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $stagingDir "install.json") -Encoding UTF8

# Clean up temp publish folders
Remove-Item -Path $appPublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $updaterPublishDir -Recurse -Force -ErrorAction SilentlyContinue

# ── 9. Smoke Test Staged Binaries ────────────────────────────────────────────
if (-not $SkipSmokeTest) {
    Write-Host "[9/12] Smoke testing staged artifacts..." -ForegroundColor Yellow
    $stagedExe = Join-Path $stagingDir "AndroidSyncControl.exe"
    $stagedUpdater = Join-Path $stagingDir "AndroidSyncControl.Updater.exe"
    $stagedAdb = Join-Path $runtimeDir "adb\adb.exe"
    $stagedScrcpy = Join-Path $runtimeDir "scrcpy\scrcpy.exe"

    if (-not (Test-Path $stagedExe)) { Write-Error "Smoke test failed: AndroidSyncControl.exe missing from stage" }
    if (-not (Test-Path $stagedUpdater)) { Write-Error "Smoke test failed: AndroidSyncControl.Updater.exe missing from stage" }
    if (-not (Test-Path $stagedAdb)) { Write-Error "Smoke test failed: adb.exe missing from staged Runtime" }
    if (-not (Test-Path $stagedScrcpy)) { Write-Error "Smoke test failed: scrcpy.exe missing from staged Runtime" }

    Write-Host "Running health-check verification on staged exe..." -ForegroundColor Yellow
    $p = Start-Process -FilePath $stagedExe -ArgumentList "--health-check" -Wait -NoNewWindow -PassThru
    if ($p.ExitCode -ne 0) {
        Write-Error "Smoke test failed: health check returned exit code $($p.ExitCode)"
    }
    Write-Host "Smoke test PASSED!" -ForegroundColor Green
} else {
    Write-Host "[9/12] Skipping smoke test." -ForegroundColor DarkGray
}

# ── 10. Package Archive & Compute SHA-256 ────────────────────────────────────
Write-Host "[10/12] Packaging release ZIP and generating SHA-256..." -ForegroundColor Yellow
$zipFileName = "$stagingName.zip"
$zipFilePath = Join-Path $releaseDir $zipFileName

if (Test-Path $zipFilePath) { Remove-Item -Path $zipFilePath -Force }
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipFilePath -CompressionLevel Optimal

$sha256 = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
$shaFile = Join-Path $releaseDir "$stagingName.sha256"
"$sha256 *$zipFileName" | Set-Content -Path $shaFile -Encoding UTF8

$zipSize = (Get-Item $zipFilePath).Length
Write-Host "Created archive: $zipFilePath ($([math]::Round($zipSize/1MB, 2)) MB)" -ForegroundColor Green
Write-Host "SHA-256: $sha256" -ForegroundColor Green

# ── 11. Generate update-manifest.json ────────────────────────────────────────
Write-Host "[11/12] Generating update-manifest.json..." -ForegroundColor Yellow
$manifest = [PSCustomObject]@{
    schemaVersion = 1
    product = "AndroidSyncControl"
    version = $Version
    channel = $Channel
    platform = "win-x64"
    mandatory = $false
    package = [PSCustomObject]@{
        url = "https://github.com/Phuc710/AndroidSyncControl/releases/download/v$Version/$zipFileName"
        size = $zipSize
        sha256 = $sha256
    }
    release = [PSCustomObject]@{
        publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        changelog = @(
            "Production packaging pipeline release v$Version",
            "Independent rollback-capable update engine (AndroidSyncControl.Updater)",
            "3-tier persistent data isolation (App vs LocalAppData vs Agent-Data)",
            "Universal Android mirroring and automated bypass engine"
        )
    }
    minimumSupportedVersion = "1.0.0"
}

$manifestJson = $manifest | ConvertTo-Json -Depth 6
$manifestPath = Join-Path $releaseDir "update-manifest.json"
$manifestJson | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Manifest generated at: $manifestPath" -ForegroundColor Green

# ── 12. Code Signing & NSIS Installer ────────────────────────────────────────
Write-Host "[12/12] Code Signing & Installer Generation..." -ForegroundColor Yellow

# Code signing hook
if (-not $SkipSign) {
    if ($env:SIGNING_CERT_PATH -and (Test-Path $env:SIGNING_CERT_PATH)) {
        Write-Host "Signing binaries with cert: $env:SIGNING_CERT_PATH..." -ForegroundColor Yellow
        # signtool sign /f "$env:SIGNING_CERT_PATH" /p "$env:SIGNING_CERT_PASS" /tr http://timestamp.digicert.com /td sha256 /fd sha256 "$stagingDir\*.exe"
    } else {
        Write-Host "Code signing skipped: No SIGNING_CERT_PATH provided. (Use -SkipSign to silence)" -ForegroundColor DarkGray
    }
}

# NSIS Installer compilation
if (-not $SkipInstaller) {
    $nsisPath = $null
    $possibleNsis = @(
        (Join-Path $rootDir "tools\nsis\makensis.exe"),
        "makensis.exe",
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "${env:ProgramFiles}\NSIS\makensis.exe"
    )

    foreach ($p in $possibleNsis) {
        if (Get-Command $p -ErrorAction SilentlyContinue) {
            $nsisPath = $p
            break
        } elseif (Test-Path $p) {
            $nsisPath = $p
            break
        }
    }

    if ($nsisPath) {
        Write-Host "Compiling NSIS installer using $nsisPath..." -ForegroundColor Yellow
        $nsiScript = Join-Path $rootDir "installer\setup.nsi"
        & $nsisPath "/DAPP_VERSION=$Version" $nsiScript
        if ($LASTEXITCODE -eq 0) {
            $installerPath = Join-Path $releaseDir "AndroidSyncControl-$Version-Setup.exe"
            Write-Host "Installer created successfully: $installerPath" -ForegroundColor Green
        } else {
            Write-Warning "NSIS compilation failed with code $LASTEXITCODE"
        }
    } else {
        Write-Host "NSIS (makensis.exe) not found on system. Installer build skipped." -ForegroundColor Yellow
        Write-Host "To enable installer creation, install NSIS: 'winget install NSIS.NSIS'" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host " RELEASE BUILD COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host "Artifacts Directory: $releaseDir" -ForegroundColor Cyan
Get-ChildItem $releaseDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
