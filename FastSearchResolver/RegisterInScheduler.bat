@echo off
:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrative privileges...
    powershell -Command "Start-Process '%~0' -Verb RunAs"
    exit /b
)

setlocal

:: Get the script's directory
set "scriptdir=%~dp0"
set "taskname=FastSearchResolver"
set "program=%scriptdir%FastSearchResolver.exe"

:: Create the scheduled task to run on any user logon
schtasks /create /tn "%taskname%" /tr "\"%program%\"" /sc onlogon /rl highest /f /it

echo Task "%taskname%" has been created successfully.
pause
