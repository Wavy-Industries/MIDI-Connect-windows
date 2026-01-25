# MIDI Connect Installer

Copyright (c) 2024 Wavy Industries AS

This directory contains the installer scripts for MIDI Connect.

## Quick Start

```powershell
.\Build-Installer.ps1
```

This will:
1. Build the app in Release mode
2. Publish it for Windows x64
3. Create the installer using Inno Setup

The output will be at: `Installer\Output\MIDI_Connect_Setup_1.0.0.exe`

## Requirements

### For Building the Installer

- **.NET SDK 10.0** or later
- **Inno Setup 6** - https://jrsoftware.org/isinfo.php

### For Running MIDI Connect

- Windows 10/11 (64-bit)
- .NET 10 Runtime (if not using self-contained deployment)
- **loopMIDI** - https://www.tobias-erichsen.de/software/loopmidi.html

## Build Options

```powershell
# Release build (default)
.\Build-Installer.ps1

# Debug build
.\Build-Installer.ps1 -Configuration Debug

# Self-contained deployment (includes .NET runtime, larger file)
.\Build-Installer.ps1 -SelfContained

# Use existing binaries (skip dotnet build)
.\Build-Installer.ps1 -SkipBuild
```

## loopMIDI Dependency

### What the Installer Does

The installer **does NOT bundle** loopMIDI or the virtualMIDI driver. Instead:

1. Checks if loopMIDI/virtualMIDI is already installed
2. Shows a dependency page explaining the requirement
3. Opens the official loopMIDI website for download
4. Allows re-checking after the user installs loopMIDI
5. Warns but allows skipping (for advanced users)

### Why Not Bundle?

The virtualMIDI SDK license prohibits redistribution without permission:

> "This software is NOT freeware or shareware... Software linking to this SDK MAY NOT BE DISTRIBUTED in any way without prior clearance."

### For Bundled Distribution

Contact Tobias Erichsen at `info@tobias-erichsen.de` for licensing.

## Support

Email: hello@wavyindustries.com
