@echo off
title Helideck Monitor — Auto-Restart Launcher
setlocal

:: Change to the folder containing this batch file (works from any working dir)
cd /d "%~dp0"

:loop
echo [%date% %time%] Starting Helideck Monitor...
start /wait HMS_01.exe
set EXIT_CODE=%errorlevel%

:: Exit code 0 = normal user shutdown (X button) — stop the loop
if %EXIT_CODE%==0 (
    echo [%date% %time%] Normal shutdown (exit 0). Launcher exiting.
    goto :eof
)

echo [%date% %time%] Helideck Monitor exited with code %EXIT_CODE%. Restarting in 5s...
timeout /t 5 /nobreak >nul
goto loop
