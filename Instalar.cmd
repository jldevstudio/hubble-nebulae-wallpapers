@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0NebulosasHubble.ps1" -Action Install
if errorlevel 1 (
    echo.
    echo No se completo la instalacion. Revisa el mensaje anterior.
    pause
    exit /b 1
)
echo.
echo Nebulosas Hubble esta activo. Windows rotara las fotos cada 30 minutos.
pause
