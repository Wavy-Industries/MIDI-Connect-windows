<#
.SYNOPSIS
    Build script for MIDI Connect installer

.DESCRIPTION
    This script builds the MIDI Connect application and creates an installer.
    
    Copyright (c) 2024 Wavy Industries AS

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)

.PARAMETER SkipBuild
    Skip the dotnet build step (use existing binaries)

.PARAMETER SelfContained
    Create self-contained deployment (includes .NET runtime)

.EXAMPLE
    .\Build-Installer.ps1
    
.EXAMPLE
    .\Build-Installer.ps1 -Configuration Debug

.NOTES
    Requirements:
    - .NET SDK 10.0 or later
    - Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
    
    IMPORTANT LICENSING NOTE:
    This installer does NOT bundle the virtualMIDI driver (loopMIDI).
    The virtualMIDI SDK license prohibits redistribution without permission.
    Users will be prompted to download loopMIDI during installation.
    For commercial distribution with bundled driver, contact: info@tobias-erichsen.de
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$SkipBuild,
    
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot
$OutputDir = Join-Path $InstallerDir "Output"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   MIDI Connect Installer Build Script" -ForegroundColor Cyan
Write-Host "   Copyright (c) 2024 Wavy Industries AS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

#region Build Application
if (-not $SkipBuild) {
    Write-Host "[1/3] Building MIDI Connect application..." -ForegroundColor Yellow
    
    $PublishArgs = @(
        "publish"
        "$ProjectRoot\MinimalWindowsApp.csproj"
        "-c", $Configuration
        "-r", "win-x64"
        "-o", "$ProjectRoot\bin\$Configuration\net10.0-windows10.0.22621.0\publish"
    )
    
    if ($SelfContained) {
        $PublishArgs += "--self-contained", "true"
        $PublishArgs += "-p:PublishSingleFile=false"
        Write-Host "  Mode: Self-contained (includes .NET runtime)" -ForegroundColor Gray
    } else {
        $PublishArgs += "--self-contained", "false"
        Write-Host "  Mode: Framework-dependent (requires .NET 10 runtime)" -ForegroundColor Gray
    }
    
    Write-Host "  Running: dotnet $($PublishArgs -join ' ')" -ForegroundColor Gray
    
    Push-Location $ProjectRoot
    try {
        & dotnet @PublishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
        Write-Host "  Build completed successfully!" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[1/3] Skipping build (using existing binaries)" -ForegroundColor Yellow
}
#endregion

#region Verify Build Output
Write-Host ""
Write-Host "[2/3] Verifying build output..." -ForegroundColor Yellow

$PublishDir = "$ProjectRoot\bin\$Configuration\net10.0-windows10.0.22621.0\publish"
$RequiredFiles = @(
    "MIDIConnect.exe"
    "MIDIConnect.dll"
    "MIDIConnect.runtimeconfig.json"
)

$MissingFiles = @()
foreach ($file in $RequiredFiles) {
    $filePath = Join-Path $PublishDir $file
    if (-not (Test-Path $filePath)) {
        $MissingFiles += $file
    }
}

if ($MissingFiles.Count -gt 0) {
    Write-Host "  ERROR: Missing required files:" -ForegroundColor Red
    foreach ($file in $MissingFiles) {
        Write-Host "    - $file" -ForegroundColor Red
    }
    throw "Build output verification failed. Run without -SkipBuild to rebuild."
}

Write-Host "  All required files present!" -ForegroundColor Green

# List files to be packaged
Write-Host "  Files to be packaged:" -ForegroundColor Gray
Get-ChildItem $PublishDir -File | ForEach-Object {
    $size = [math]::Round($_.Length / 1KB, 2)
    Write-Host "    - $($_.Name) ($size KB)" -ForegroundColor Gray
}
#endregion

#region Create Installer
Write-Host ""
Write-Host "[3/3] Creating installer..." -ForegroundColor Yellow

# Find Inno Setup compiler
$InnoSetupPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$ISCC = $null
foreach ($path in $InnoSetupPaths) {
    if (Test-Path $path) {
        $ISCC = $path
        break
    }
}

if (-not $ISCC) {
    Write-Host ""
    Write-Host "  Inno Setup 6 not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Please install Inno Setup 6 from:" -ForegroundColor Yellow
    Write-Host "  https://jrsoftware.org/isinfo.php" -ForegroundColor Cyan
    throw "Inno Setup not found"
}

Write-Host "  Using Inno Setup: $ISCC" -ForegroundColor Gray

$IssFile = Join-Path $InstallerDir "MIDI_Connect_Setup.iss"

& $ISCC $IssFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}
#endregion

#region Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Installer location:" -ForegroundColor White
Get-ChildItem $OutputDir -Filter "MIDI_Connect_Setup*" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.FullName) ($size MB)" -ForegroundColor Cyan
}
Write-Host ""
Write-Host "IMPORTANT NOTES:" -ForegroundColor Yellow
Write-Host "  - This installer will prompt users to install loopMIDI" -ForegroundColor Gray
Write-Host "  - loopMIDI provides the virtualMIDI driver required by this app" -ForegroundColor Gray
Write-Host "  - For bundled driver distribution, contact info@tobias-erichsen.de" -ForegroundColor Gray
Write-Host ""
Write-Host "Support: hello@wavyindustries.com" -ForegroundColor Gray
Write-Host ""
#endregion
