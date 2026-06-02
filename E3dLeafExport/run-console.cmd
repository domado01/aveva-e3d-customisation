@echo off
chcp 65001 >nul
echo === AVEVA Leaf Export (Console) ===
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0build-and-run.ps1" %*
echo.
pause
