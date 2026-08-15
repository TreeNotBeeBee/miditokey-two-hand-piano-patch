@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Apply-Patch.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Read the error above; no unsupported executable was overwritten.
  pause
  exit /b 1
)
echo.
pause
