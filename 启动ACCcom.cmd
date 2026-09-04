@echo off
rem ============================================================
rem  ACCcom 串口调试助手 — 一键启动
rem  双击本脚本即启动应用；若 Release 产物缺失则先构建。
rem ============================================================
setlocal
cd /d "%~dp0"

set "EXE=src\ACCcom\bin\Release\net8.0-windows\ACCcom.exe"

if not exist "%EXE%" (
    echo [启动] Release 产物不存在，先构建...
    call dotnet build src\ACCcom\ACCcom.csproj -c Release
    if errorlevel 1 (
        echo [错误] 构建失败，请检查 .NET 8 SDK。
        pause
        exit /b 1
    )
)

echo [启动] %EXE%
start "" "%EXE%"
endlocal
