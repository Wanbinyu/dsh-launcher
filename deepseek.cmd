@echo off
setlocal
set "DSH_LAUNCHER_DIR=%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSH_LAUNCHER_DIR%dsh-launcher.ps1" -CommandName deepseek %*
set "DSH_EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %DSH_EXIT_CODE%
