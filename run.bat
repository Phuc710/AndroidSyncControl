@echo off
setlocal

echo ========================================================
echo   ANDROIDSYNCCONTROL - ZERO STALE CODE LAUNCHER
echo ========================================================

:: 1. Auto kill all old instances and zombie scrcpy streams
taskkill /f /im AndroidSyncControl.exe >nul 2>&1
taskkill /f /im scrcpy.exe >nul 2>&1

:: 2. Auto incremental build
echo [*] Checking and building latest code changes...
dotnet build "%~dp0src\AndroidSyncControl\AndroidSyncControl.csproj" -c Release -p:Platform=x64 --nologo -v q
if %errorlevel% neq 0 (
    echo [!] Build failed! Please check errors above.
    pause
    exit /b %errorlevel%
)

:: 3. Sync to Program Files if installed there (ensures Start Menu / shortcuts never run stale code)
set "APP_DIR=%~dp0src\AndroidSyncControl\bin\x64\Release\net8.0-windows\win-x64"
set "PROG_DIR=C:\Program Files\AndroidSyncControl"
if exist "%PROG_DIR%" (
    xcopy /y /q "%APP_DIR%\AndroidSyncControl.*" "%PROG_DIR%\" >nul 2>&1
)

:: 4. Launch the freshly compiled x64 Release binary
if exist "%APP_DIR%\AndroidSyncControl.exe" (
    echo [+] Launching freshly compiled AndroidSyncControl...
    start "" /D "%APP_DIR%" "%APP_DIR%\AndroidSyncControl.exe"
) else (
    dotnet run --project "%~dp0src\AndroidSyncControl\AndroidSyncControl.csproj" -c Release -p:Platform=x64
)
endlocal
exit /b 0
