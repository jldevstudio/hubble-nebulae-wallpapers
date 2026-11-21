@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0HubbleNebulae.ps1" -Action Install
if errorlevel 1 (
    echo.
    echo Installation failed. See the message above.
    pause
    exit /b 1
)
echo.
echo Hubble Nebulae Wallpapers is active. Windows will change the wallpaper every 30 minutes.
pause
