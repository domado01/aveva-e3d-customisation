@echo off
echo === Find PDMS project environment (projects_dir, project paths) ===
echo.
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0find-project-env.ps1"
echo.
pause
