@echo off
chcp 65001 >nul
echo === AVEVA Marine Leaf Export - 입력 폼 빌드+실행 ===
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0form-build-and-run.ps1" %*
echo.
pause
