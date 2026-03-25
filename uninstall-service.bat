@echo off
echo ============================================
echo   COMcopier Windows Service Uninstaller
echo ============================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Please run as Administrator.
    pause
    exit /b 1
)

set SERVICE_NAME=COMcopier

echo Stopping service...
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul
echo Removing service...
sc delete %SERVICE_NAME%

echo.
echo Service removed successfully.
pause
