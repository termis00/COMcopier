@echo off
echo ============================================
echo   COMcopier Windows Service Installer
echo ============================================
echo.

:: Check admin privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run as Administrator.
    echo         Right-click this file and select "Run as administrator".
    pause
    exit /b 1
)

set SERVICE_NAME=COMcopier
set EXE_PATH=%~dp0COMcopier.exe

if not exist "%EXE_PATH%" (
    echo [ERROR] COMcopier.exe not found.
    echo         This file must be in the same folder as COMcopier.exe.
    pause
    exit /b 1
)

:: Stop and remove existing service
echo [1/2] Checking existing service...
sc query %SERVICE_NAME% >nul 2>&1
if %errorlevel% equ 0 (
    echo Stopping existing service...
    sc stop %SERVICE_NAME% >nul 2>&1
    timeout /t 3 /nobreak >nul
    sc delete %SERVICE_NAME%
    timeout /t 2 /nobreak >nul
)

:: Register service
echo [2/2] Registering service...
sc create %SERVICE_NAME% binPath= "%EXE_PATH%" start= auto displayname= "COMcopier - COM Port Copier"
sc description %SERVICE_NAME% "Copies POS COM port output to multiple printer ports."
sc start %SERVICE_NAME%

echo.
echo ============================================
echo   Installation complete!
echo   Service name : %SERVICE_NAME%
echo   Executable   : %EXE_PATH%
echo   Config file  : %~dp0appsettings.json
echo   Config tool  : %~dp0COMcopier.ConfigUI.exe
echo ============================================
echo.
echo   1. Run COMcopier.ConfigUI.exe to configure.
echo   2. After changing settings, restart the service:
echo        sc stop %SERVICE_NAME%
echo        sc start %SERVICE_NAME%
echo      (or use the "Restart Service" button in the config tool)
echo.
pause
