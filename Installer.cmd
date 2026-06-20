@echo off
REM CoreKeeperAccess - double-click installer for testers.
REM Runs install.ps1 with the execution policy bypassed so no command line is
REM ever needed, and keeps this window open at the end so a screen reader can
REM read the result.

echo Installing the Core Keeper accessibility mod...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

echo.
echo Press any key to close this window.
pause >nul
