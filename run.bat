@echo off
chcp 65001 >nul
echo ========================================================
echo   ANDROIDSYNCCONTROL - UNIVERSAL PHONE CONTROL
echo ========================================================
set "EXE_BIN=%~dp0src\AndroidSyncControl\bin\x64\Debug\net8.0-windows\AndroidSyncControl.exe"
if exist "%EXE_BIN%" (
    start "" /D "%~dp0src\AndroidSyncControl\bin\x64\Debug\net8.0-windows" "%EXE_BIN%"
) else (
    dotnet run --project "%~dp0src\AndroidSyncControl\AndroidSyncControl.csproj"
)
exit
