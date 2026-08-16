@echo off
setlocal
set "DSH_LAUNCHER_DIR=%~dp0"
if exist "%DSH_LAUNCHER_DIR%dsh-launcher.exe" (
    if "%~1"=="" goto background
    if /I "%~1"=="start" if "%~2"=="" goto background
    if /I "%~1"=="stop" if "%~2"=="" goto background
    if /I "%~1"=="restart" if "%~2"=="" goto background
    if /I "%~1"=="status" if "%~2"=="" goto background
    if /I "%~1"=="open" if "%~2"=="" goto background
    if /I "%~1"=="logs" if "%~2"=="" goto background
    if /I "%~1"=="--foreground" goto foreground
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DSH_LAUNCHER_DIR%dsh-launcher.ps1" -CommandName dsh %*
set "DSH_EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %DSH_EXIT_CODE%

:background
start "" /b "%DSH_LAUNCHER_DIR%dsh-launcher.exe" %*
set "DSH_EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %DSH_EXIT_CODE%

:foreground
"%DSH_LAUNCHER_DIR%dsh-launcher.exe" %*
set "DSH_EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %DSH_EXIT_CODE%
