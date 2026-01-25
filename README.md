# MIDI Toolbar for Windows - MVP Status

## Current State
The MVP is **80% complete**. The basic structure is in place with Bluetooth LE scanning, device discovery, connection UI, and MIDI transmission infrastructure.

## What's Working

### ✅ Completed Features
1. **System Tray Application** - WPF app with Hardcodet.NotifyIcon.Wpf
2. **Bluetooth LE Scanning** - DeviceWatcher for discovering BLE devices
3. **Device List UI** - Shows:
   - Connected devices (with Disconnect button)
   - Discovered devices (with Connect button)
4. **Connection Logic** - Connect/Disconnect to BLE MIDI devices
5. **MIDI Port Infrastructure** - MidiManager opens ports with notifications
6. **MIDI Characteristic Discovery** - Finds Apple MIDI service (03B80E5A...) and I/O characteristic (7772E5DB...)

### 🔄 Still In Progress
1. **Windows MIDI API Integration** - The critical next step is to:
   - Research Windows MIDI APIs (WinMM or WinRT MIDI)
   - Create virtual MIDI ports for connected devices
   - Route MIDI data between Windows apps and BLE devices

## Architecture

### Core Managers (Singleton Pattern)
```
Managers/
├── BluetoothManager.cs    - BLE scanning & GATT connection
├── MidiManager.cs          - MIDI port management
├── DeviceSession.cs        - Represents a device connection
└── MainViewModel.cs        - UI binding
```

### Bluetooth MIDI Service UUIDs
- **Service**: `03B80E5A-EDE8-4B33-A751-6CE34EC4C700` (Apple MIDI)
- **Characteristic**: `7772E5DB-3868-4112-A1A9-F2669D106BF3` (MIDI I/O)

### UI Structure
```
MainWindow.xaml
├── Connected Devices List
│   └── Shows connected BLE MIDI devices
└── Discovered Devices List
    └── Shows BLE devices found during scanning
```

## How It Works
1. App starts → System tray icon appears
2. Bluetooth scanning begins automatically
3. Discovered devices appear in "Discovered Devices" list
4. Click "Connect" → Device moves to "Connected Devices" list
5. MIDI port opens with notification handler
6. When MIDI data arrives → ValueChanged event triggered
7. **TODO**: Forward to Windows MIDI port

## Next Steps

### Priority 1: Research & Implement Windows MIDI APIs
Need to choose between:
- **WinMM** (Legacy, broader compatibility)
- **Windows.Devices.Midi** (Modern WinRT, Windows 10+)

Required functionality:
- Create virtual MIDI ports for each BLE device
- Receive MIDI from BLE → Send to Windows MIDI Out
- Receive MIDI from Windows MIDI In → Send to BLE

### Priority 2: Testing
- Test with real Bluetooth MIDI device
- Verify MIDI data flows correctly
- Test reconnect/disconnect scenarios

### Priority 3: MVP Polish
- Better error handling
- Connection state visualization
- Device names in UI
- Stop/start scanning controls

## Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

## Dependencies
- .NET 10.0 + Windows 10.0.22621.0 (Windows 11 SDK)
- Hardcodet.NotifyIcon.Wpf v1.1.0
- Microsoft.Windows.SDK.Contracts v10.0.22621.2428
- System.Text.Json v9.0.0

## Known Issues
- **Build may show LSP errors** for Windows Runtime types (Windows.Devices namespace). This is expected as the LSP server doesn't load WinRT types, but the build should work with proper SDK references.
- **MIDI routing not implemented yet** - This is the critical next step

## File Structure
```
MinimalWindowsApp/
├── App.xaml / App.xaml.cs              # System tray setup
├── MainWindow.xaml / MainWindow.xaml.cs # UI
├── MainViewModel.cs                    # UI binding logic
├── Managers/
│   ├── BluetoothManager.cs            # BLE scanning & GATT
│   ├── MidiManager.cs                 # MIDI port management
│   └── DeviceSession.cs               # Device connection state
└── MinimalWindowsApp.csproj           # Project file
```

## Timeline Estimate

Remaining work:
- **Research Windows MIDI APIs**: 2-4 hours
- **Implement MIDI routing**: 4-6 hours  
- **Testing & debugging**: 4-8 hours

**Total**: ~2 days to complete MVP

## Comparison with macOS Version

| Feature | macOS | Windows MVP |
|---------|-------|-------------|
| System tray | ✅ MenuBarExtra | ✅ System tray icon |
| BLE scanning | ✅ CoreBluetooth | ✅ DeviceWatcher |
| Device list | ✅ Known + Other | ✅ Connected + Discovered |
| Connect/Disconnect | ✅ | ✅ |
| MIDI routing | ✅ CoreMIDI | ❌ Not yet |
| Persistence | ✅ UserDefaults | ❌ Not yet (not in MVP scope) |
| Battery info | ✅ | ❌ Not yet |

## Testing Checklist

When MIDI routing is ready:
- [ ] Device appears in "Discovered" list
- [ ] Click Connect → Moves to "Connected" list
- [ ] Windows creates MIDI port for device
- [ ] MIDI data flows from BLE to Windows MIDI
- [ ] MIDI data flows from Windows MIDI to BLE
- [ ] Disconnect cleans up properly
- [ ] Reconnect works reliably
- [ ] Multiple devices connect simultaneously
