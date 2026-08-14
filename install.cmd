@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "DSH_EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %DSH_EXIT_CODE%
