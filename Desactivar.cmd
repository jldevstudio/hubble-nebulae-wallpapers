@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0NebulosasHubble.ps1" -Action Stop
if errorlevel 1 (
    echo.
    echo No se pudo quitar la sincronizacion automatica.
    pause
    exit /b 1
)
echo.
echo Sincronizacion automatica desactivada.
echo Para quitar la rotacion, elegi otro fondo en Personalizacion de Windows.
pause
