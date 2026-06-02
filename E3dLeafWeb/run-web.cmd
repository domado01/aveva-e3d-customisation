@echo off
chcp 65001 >nul
echo === AVEVA Leaf Export (Web / Standalone host) ===
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0web-build-and-run.ps1" %*
echo.
pause
