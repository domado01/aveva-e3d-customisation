@echo off
REM ============================================================
REM  run-streamlit.cmd - AVEVA Marine Leaf Export (web UI)
REM  1) build E3dLeafCli.exe (.NET engine)
REM  2) ensure Python + streamlit
REM  3) launch the web app in your browser
REM  Run on the AVEVA Marine PC with AM/PDMS running and logged in.
REM ============================================================
setlocal
set HERE=%~dp0
cd /d "%HERE%"

echo.
echo [1/3] Building E3dLeafCli.exe (.NET engine)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%..\E3dLeafCli\build-cli.ps1"
if errorlevel 1 (
  echo.
  echo [WARN] CLI build failed or AVEVA not found. The web UI will still open,
  echo        but extraction needs E3dLeafCli.exe. Fix the build then retry.
  echo.
)

echo.
echo [2/3] Checking Python and streamlit...
set PY=python
where python >nul 2>nul || set PY=py
%PY% --version >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Python not found. Install Python 3 from python.org or Microsoft Store,
  echo         then run this file again.
  pause
  exit /b 1
)
%PY% -c "import streamlit" >nul 2>nul
if errorlevel 1 (
  echo Installing streamlit ^(first run only^)...
  %PY% -m pip install --user streamlit
  if errorlevel 1 (
    echo [ERROR] streamlit install failed. Check network/proxy or corporate policy.
    pause
    exit /b 1
  )
)

echo.
echo [3/3] Launching web app... a browser tab will open.
echo       Press Ctrl+C in this window to stop.
echo.
%PY% -m streamlit run "%HERE%app.py"

pause
endlocal
