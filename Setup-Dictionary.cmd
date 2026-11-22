@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Dictionary.ps1" %*
set "setupExitCode=%ERRORLEVEL%"
pause
exit /b %setupExitCode%
