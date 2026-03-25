@echo off
echo ============================================
echo   COMcopier - Virtual COM Port Setup
echo ============================================
echo.
echo   This will install the com0com virtual COM port driver
echo   and create a port pair (default: COM10 - COM11).
echo.
echo   Admin privileges required.
echo.

:: Check admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: Run PowerShell script from same directory
powershell -ExecutionPolicy Bypass -File "%~dp0setup-virtual-ports.ps1" %*
