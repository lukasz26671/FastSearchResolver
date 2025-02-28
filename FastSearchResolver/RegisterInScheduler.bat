@echo off
:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrative privileges...
    powershell -Command "Start-Process '%~0' -Verb RunAs"
    exit /b
)

setlocal

:: Define paths
set "scriptdir=%~dp0"
set "scriptdir=%scriptdir:~0,-1%"  :: Remove trailing backslash
set "taskname=FastSearchResolver"
set "program=%scriptdir%\FastSearchResolver.exe"

:: Create the scheduled task with correct "Start In" and hidden execution
powershell -Command ^
$Action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-WindowStyle Hidden -Command Start-Process \"%program%\" -WorkingDirectory \"%scriptdir%\" -WindowStyle Hidden'; ^
$Trigger = New-ScheduledTaskTrigger -AtLogOn; ^
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest; ^
$Task = New-ScheduledTask -Action $Action -Trigger $Trigger -Principal $Principal; ^
Register-ScheduledTask -TaskName '%taskname%' -InputObject $Task -Force"

echo Task "%taskname%" has been created successfully and will run in the background.
pause
