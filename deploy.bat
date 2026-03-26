@echo off
echo ============================================
echo   COMcopier - Build Deploy Package
echo ============================================
echo.

set DEPLOY_DIR=%~dp0deploy

:: Accept target architecture as parameter: deploy.bat x86 or deploy.bat x64
if /i "%~1"=="x86" (
    set RUNTIME=win-x86
) else if /i "%~1"=="x64" (
    set RUNTIME=win-x64
) else if "%~1"=="" (
    :: Default: detect build machine architecture
    if "%PROCESSOR_ARCHITECTURE%"=="x86" (
        if not defined PROCESSOR_ARCHITEW6432 (
            set RUNTIME=win-x86
        ) else (
            set RUNTIME=win-x64
        )
    ) else (
        set RUNTIME=win-x64
    )
) else (
    echo [ERROR] Unknown architecture: %~1
    echo         Usage: deploy.bat [x86^|x64]
    pause
    exit /b 1
)
echo Target runtime: %RUNTIME%

:: Find dotnet SDK - check common install locations
set DOTNET_FOUND=0
where dotnet >nul 2>nul && set DOTNET_FOUND=1
if "%DOTNET_FOUND%"=="0" if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%" & set DOTNET_FOUND=1
if "%DOTNET_FOUND%"=="0" if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%" & set DOTNET_FOUND=1
if "%DOTNET_FOUND%"=="0" (
    echo [ERROR] dotnet SDK not found
    echo         Install from: https://dot.net/download
    pause
    exit /b 1
)
echo Using dotnet SDK:
dotnet --version
echo.

:: Clean previous deploy
if exist "%DEPLOY_DIR%" rmdir /s /q "%DEPLOY_DIR%"
mkdir "%DEPLOY_DIR%"

:: Build service (self-contained, single file, compressed)
echo [1/2] Building service...
dotnet publish "%~dp0COMcopier.csproj" -c Release -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "%DEPLOY_DIR%"
if %errorlevel% neq 0 (
    echo [ERROR] Service build failed.
    pause
    exit /b 1
)

:: Build ConfigUI (self-contained, single file, compressed)
echo [2/2] Building config tool...
dotnet publish "%~dp0COMcopier.ConfigUI\COMcopier.ConfigUI.csproj" -c Release -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "%DEPLOY_DIR%"
if %errorlevel% neq 0 (
    echo [ERROR] Config tool build failed.
    pause
    exit /b 1
)

:: Copy scripts and clean up
copy "%~dp0install-service.bat" "%DEPLOY_DIR%\" >nul
copy "%~dp0uninstall-service.bat" "%DEPLOY_DIR%\" >nul
copy "%~dp0setup-virtual-ports.bat" "%DEPLOY_DIR%\" >nul
copy "%~dp0setup-virtual-ports.ps1" "%DEPLOY_DIR%\" >nul
copy "%~dp0com0com-3.0.0.0-i386-and-x64-signed.zip" "%DEPLOY_DIR%\" >nul
del /q "%DEPLOY_DIR%\*.pdb" 2>nul
del /q "%DEPLOY_DIR%\appsettings.Development.json" 2>nul
del /q "%DEPLOY_DIR%\*.runtimeconfig.json" 2>nul
rmdir /q "%DEPLOY_DIR%\deploy" 2>nul

echo.
echo ============================================
echo   Deploy package created!
echo   Location: %DEPLOY_DIR%
echo ============================================
echo.
echo   Copy the deploy folder to the POS machine, then:
echo.
echo   Step 1. setup-virtual-ports.bat  (install virtual COM ports)
echo   Step 2. COMcopier.ConfigUI.exe   (configure port mappings)
echo   Step 3. install-service.bat       (install Windows service)
echo.
pause
