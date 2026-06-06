@echo off
chcp 65001 >nul
echo === AVEVA Type Namespace Check (32-bit) ===
echo.
rem 32비트 PowerShell 로 실행 (Marine DLL 이 x86 일 때 LoadFrom 폴백이 동작하도록)
if exist "%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" (
  "%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -NoProfile -File "%~dp0check-types.ps1"
) else (
  powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0check-types.ps1"
)
echo.
echo (결과는 위 화면과 바탕화면 aveva_types.txt 에 있습니다)
pause
