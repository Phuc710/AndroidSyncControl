@echo off
chcp 65001 >nul
echo ========================================================
echo   ANDROIDSYNCCONTROL - UNIVERSAL PHONE CONTROL
echo ========================================================
set "APP_DIR=%~dp0src\AndroidSyncControl\bin\x64\Release\net8.0-windows\win-x64"
if not exist "%APP_DIR%\AndroidSyncControl.exe" set "APP_DIR=%~dp0src\AndroidSyncControl\bin\Release\net8.0-windows\win-x64"
if not exist "%APP_DIR%\AndroidSyncControl.exe" set "APP_DIR=%~dp0src\AndroidSyncControl\bin\x64\Debug\net8.0-windows\win-x64"
if not exist "%APP_DIR%\AndroidSyncControl.exe" set "APP_DIR=%~dp0src\AndroidSyncControl\bin\Debug\net8.0-windows\win-x64"
if not exist "%APP_DIR%\AndroidSyncControl.exe" set "APP_DIR=%~dp0dist\AndroidSyncControl-1.0.0-win-x64"

if exist "%APP_DIR%\AndroidSyncControl.exe" (
    start "" /D "%APP_DIR%" "%APP_DIR%\AndroidSyncControl.exe"
) else (
    dotnet run --project "%~dp0src\AndroidSyncControl\AndroidSyncControl.csproj"
)
exit
