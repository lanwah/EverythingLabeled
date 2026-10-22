@echo off
setlocal
rem UTF-8 console so build-ext.ps1 Chinese messages display correctly in cmd.
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-ext.ps1" %*
exit /b %ERRORLEVEL%
