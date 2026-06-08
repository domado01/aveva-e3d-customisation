@echo off
chcp 65001 >nul
echo === Find Standalone class in Aveva.Pdms.Standalone.dll (32-bit) ===
echo.
if exist "%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" (
  "%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -NoProfile -File "%~dp0find-standalone.ps1"
) else (
  powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0find-standalone.ps1"
)
echo.
pause
