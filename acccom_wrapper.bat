@echo off
set DOTNET_ENABLE_DIAGNOSTICS=1
set COMPlus_DbgEnableMiniDump=1
set COMPlus_DbgMiniDumpType=4
if exist "%~dp0src\ACCcom\bin\Release\net8.0-windows\ACCcom.exe" (
    "%~dp0src\ACCcom\bin\Release\net8.0-windows\ACCcom.exe" %*
) else if exist "%~dp0src\ACCcom\bin\Debug\net8.0-windows\ACCcom.exe" (
    "%~dp0src\ACCcom\bin\Debug\net8.0-windows\ACCcom.exe" %*
) else (
    echo [ERROR] ACCcom.exe not found. Run 'dotnet build' first.
    exit /b 1
)
echo EXIT CODE: %ERRORLEVEL%
