@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0HubbleNebulae.ps1" -Action Stop
if errorlevel 1 (
    echo.
    echo Could not remove the scheduled sync.
    pause
    exit /b 1
)
echo.
echo Automatic sync is disabled.
echo To stop the slideshow, choose a different background in Windows Personalization.
pause
