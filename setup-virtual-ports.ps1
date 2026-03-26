#Requires -RunAsAdministrator
# COMcopier - Virtual COM Port Setup Script
# com0com driver install and port pair creation

param(
    [string]$PortA = "COM10",
    [string]$PortB = "COM11"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  COMcopier - Virtual COM Port Setup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Port pair: $PortA <-> $PortB"
Write-Host "  POS output  -> $PortA"
Write-Host "  COMcopier   <- $PortB"
Write-Host ""

# --- Paths ---
$is64bit = [Environment]::Is64BitOperatingSystem
$com0comDir = $null
$setupcPath = $null
if ($is64bit) {
    $searchDirs = @("${env:ProgramFiles}\com0com", "${env:ProgramFiles(x86)}\com0com")
} else {
    $searchDirs = @("${env:ProgramFiles}\com0com")
}
foreach ($dir in $searchDirs) {
    if (Test-Path "$dir\setupc.exe") {
        $com0comDir = $dir
        $setupcPath = "$dir\setupc.exe"
        break
    }
}
$zipPath = Join-Path $PSScriptRoot "com0com-3.0.0.0-i386-and-x64-signed.zip"
$extractPath = Join-Path $env:TEMP "com0com-extract"

# --- Check if already installed ---
if ($setupcPath -and (Test-Path $setupcPath)) {
    Write-Host "[OK] com0com is already installed." -ForegroundColor Green
}
else {
    # Verify ZIP exists
    if (-not (Test-Path $zipPath)) {
        Write-Host "[ERROR] com0com ZIP not found: $zipPath" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }

    Write-Host "[1/2] Extracting com0com..." -ForegroundColor Yellow
    if (Test-Path $extractPath) { Remove-Item $extractPath -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    Write-Host "[2/2] Installing com0com driver..." -ForegroundColor Yellow
    Write-Host "  A Windows security prompt may appear - please accept the driver." -ForegroundColor Yellow
    Write-Host ""

    # Find installer exe matching OS architecture
    # com0com ZIP contains: Setup_com0com_v3.0.0.0_W7_x64_signed.exe / x86 variant
    if ($is64bit) {
        $preferArch = "x64"
    } else {
        $preferArch = "x86"
    }
    $setupExe = Get-ChildItem -Path $extractPath -Filter "*.exe" -Recurse |
                Where-Object { $_.Name -match $preferArch } |
                Select-Object -First 1

    if (-not $setupExe) {
        $setupExe = Get-ChildItem -Path $extractPath -Filter "*.exe" -Recurse |
                    Select-Object -First 1
    }

    if (-not $setupExe) {
        Write-Host "[ERROR] Installer exe not found in archive." -ForegroundColor Red
        Write-Host "  Contents:" -ForegroundColor Gray
        Get-ChildItem -Path $extractPath -Recurse | ForEach-Object { Write-Host "    $($_.Name)" -ForegroundColor Gray }
        Read-Host "Press Enter to exit"
        exit 1
    }

    Write-Host "  Running: $($setupExe.FullName)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  *** The com0com installer will open. ***" -ForegroundColor White
    Write-Host "  *** Please complete the installation, then come back here. ***" -ForegroundColor White
    Write-Host ""

    Start-Process -FilePath $setupExe.FullName -Wait
    Start-Sleep -Seconds 2

    # Cleanup
    Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue

    # Re-scan for setupc.exe after installation
    foreach ($dir in $searchDirs) {
        if (Test-Path "$dir\setupc.exe") {
            $com0comDir = $dir
            $setupcPath = "$dir\setupc.exe"
            break
        }
    }

    if (-not $setupcPath -or -not (Test-Path $setupcPath)) {
        Write-Host "[ERROR] com0com not detected after installation." -ForegroundColor Red
        Write-Host "  Searched: Program Files, Program Files (x86)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  If you installed to a different location," -ForegroundColor Yellow
        Write-Host "  please create the port pair manually using com0com Setup." -ForegroundColor Yellow
        Read-Host "Press Enter to exit"
        exit 1
    }

    Write-Host "[OK] com0com installed successfully." -ForegroundColor Green
}

# --- Create virtual port pair ---
Write-Host ""
Write-Host "Creating virtual port pair: $PortA <-> $PortB ..." -ForegroundColor Yellow

# setupc.exe must run from its own directory to find com0com.inf
Push-Location $com0comDir
& $setupcPath install PortName=$PortA PortName=$PortB 2>&1 | Out-String | Write-Host
Pop-Location

# Verify - check both SerialPort API and com0com's own list
Write-Host "Verifying..." -ForegroundColor Yellow

# com0com's own port list is authoritative
Push-Location $com0comDir
$com0comList = & $setupcPath list 2>&1 | Out-String
Pop-Location
$registeredA = $com0comList -match $PortA
$registeredB = $com0comList -match $PortB

# Also check OS-level visibility
$ports = [System.IO.Ports.SerialPort]::GetPortNames()
$visibleA = $ports -contains $PortA
$visibleB = $ports -contains $PortB

Write-Host ""
if ($registeredA -and $registeredB) {
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Port pair registered successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  $PortA <-> $PortB" -ForegroundColor Cyan
    Write-Host ""

    if (-not $visibleA -or -not $visibleB) {
        Write-Host "  Ports are registered but not yet visible to the OS." -ForegroundColor Yellow
        Write-Host "  >>> Please REBOOT to activate the virtual ports. <<<" -ForegroundColor Red
        Write-Host ""
        Write-Host "  After reboot:" -ForegroundColor White
    }
    else {
        Write-Host "  Ports are active and ready." -ForegroundColor Green
        Write-Host ""
    }

    Write-Host "    1. Set POS printer output to $PortA"
    Write-Host "    2. Set COMcopier source port to $PortB"
    Write-Host ""
}
else {
    Write-Host "[ERROR] Port pair creation failed." -ForegroundColor Red
    Write-Host ""
    Write-Host "  com0com status:" -ForegroundColor Gray
    Write-Host $com0comList -ForegroundColor Gray
    Write-Host ""
}

Read-Host "Press Enter to exit"
