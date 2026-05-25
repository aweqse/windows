@echo off
cd /d "%~dp0"

real_time.exe --date 20260523 --snapshot-min 5 --output-dir C:\Users\dev-w\Desktop\workspace\output

echo.
echo exit code = %ERRORLEVEL%
