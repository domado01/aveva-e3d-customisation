@echo off
echo === AVEVA Marine Leaf Export - input form (build + run) ===
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0form-build-and-run.ps1" %*
echo.
pause
