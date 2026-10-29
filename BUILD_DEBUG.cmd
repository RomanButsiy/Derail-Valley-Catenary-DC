@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build.ps1" -Configuration Debug
set "RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %RESULT%
