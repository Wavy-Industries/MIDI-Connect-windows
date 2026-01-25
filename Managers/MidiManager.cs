using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Midi;
using Windows.Devices.Enumeration;

namespace MinimalWindowsApp.Managers
{
    public class MidiManager
    {
        private static MidiManager? _instance;
        public static MidiManager Instance => _instance ??= new MidiManager();
        
        private readonly Dictionary<string, MidiPort> _midiPorts = new();
        private readonly Dictionary<ulong, IMidiOutPort> _virtualMidiPorts = new();
        private readonly Dictionary<ulong, VirtualMidiPort> _teVirtualMidiPorts = new();
        private uint _timestampCounter = 0;
        
        static MidiManager()
        {
            try
            {
                Logger.Info("[DRIVER CHECK] Checking Windows MIDI driver versions...");
                var selector = MidiOutPort.GetDeviceSelector();
                var devicesTask = DeviceInformation.FindAllAsync(selector);
                devicesTask.AsTask().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        var devices = t.Result;
                        Logger.Info($"[DRIVER CHECK] Found {devices.Count} MIDI devices");
                        foreach (var device in devices)
                        {
                            Logger.Info($"  - {device.Name} (ID: {device.Id})");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[DRIVER CHECK] Exception during driver version check: {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
            }
        }
        
        private MidiManager()
        {
        }
        
        internal ushort GetNextTimestamp()
        {
            _timestampCounter = (_timestampCounter + 1) & 0x1FFF;
            var timestamp = (ushort)_timestampCounter;
            Logger.LogDebug($"Generated MIDI timestamp: 0x{timestamp:X4}");
            return timestamp;
        }
        
        public void RegisterVirtualMidiPort(ulong bluetoothAddress, VirtualMidiPort port)
        {
            _teVirtualMidiPorts[bluetoothAddress] = port;
            Logger.Info($"Registered virtual MIDI port for device 0x{bluetoothAddress:X}");
        }
        
        public void UnregisterVirtualMidiPort(ulong bluetoothAddress)
        {
            _teVirtualMidiPorts.Remove(bluetoothAddress);
            Logger.Info($"Unregistered virtual MIDI port for device 0x{bluetoothAddress:X}");
        }
        
        public VirtualMidiPort? GetTeVirtualMidiPort(ulong bluetoothAddress)
        {
            _teVirtualMidiPorts.TryGetValue(bluetoothAddress, out var port);
            return port;
        }
        
        public BluetoothDeviceInfo? GetDeviceInfo(ulong bluetoothAddress)
        {
            // Get from BluetoothManager's discovered devices
            return BluetoothManager.Instance.GetDeviceInfo(bluetoothAddress);
        }
        
        public async Task<IMidiOutPort> CreateVirtualMidiPort(string deviceName, ulong bluetoothAddress)
        {
            try
            {
                Logger.Info($"Creating virtual MIDI port for '{deviceName}' (BLE: {bluetoothAddress:X})");
                
                string selector = MidiOutPort.GetDeviceSelector();
                var devices = await DeviceInformation.FindAllAsync(selector);
                
                if (devices == null)
                {
                    Logger.Error("[ERROR] DeviceInformation.FindAllAsync returned null devices list");
                    return null;
                }
                
                if (devices.Count == 0)
                {
                    Logger.Error("[ERROR] No MIDI output devices found in Windows enumeration");
                    return null;
                }
                
                Logger.Info($"[INFO] Found {devices.Count} MIDI devices in Windows enumeration");
                
                var cleanedDeviceName = deviceName.Replace(" ", "");
                var targetDevice = devices.FirstOrDefault(d => d.Name.Contains(cleanedDeviceName));
                
                if (targetDevice == null)
                {
                    Logger.Warning($"[WARNING] Target device '{deviceName}' not found in enumeration");
                    Logger.Info("[INFO] Available devices (first 10):");
                    foreach (var dev in devices.Take(10))
                    {
                        Logger.Info($"  - {dev.Name}");
                    }
                    return null;
                }
                
                Logger.Info($"[SUCCESS] Found target device: {targetDevice.Name}");
                
                var port = await MidiOutPort.FromIdAsync(targetDevice.Id);
                
                if (port == null)
                {
                    Logger.Error($"[ERROR] MidiOutPort.FromIdAsync returned null for device ID: {targetDevice.Id}");
                    return null;
                }
                
                _virtualMidiPorts[bluetoothAddress] = port;
                
                Logger.Info($"[VERIFY] Checking Windows MIDI enumeration for virtual port...");
                
                var verifyDevices = await DeviceInformation.FindAllAsync(selector);
                
                if (verifyDevices == null)
                {
                    Logger.Error($"[VERIFY ERROR] DeviceInformation.FindAllAsync returned null during verification");
                    return port;
                }
                
                var ourDevice = verifyDevices.FirstOrDefault(d => d.Name.Contains(cleanedDeviceName));
                
                if (ourDevice != null)
                {
                    Logger.Info($"[SUCCESS] Virtual port '{ourDevice.Name}' FOUND in Windows MIDI devices!");
                    Logger.Info($"  - Device ID: {ourDevice.Id}");
                    Logger.Info($"  - Is Enabled: {ourDevice.IsEnabled}");
                    Logger.Info($"  - Total devices in enumeration: {verifyDevices.Count}");
                }
                else
                {
                    Logger.Warning($"[ISSUE] Virtual port '{deviceName}' NOT found in Windows MIDI enumeration");
                    Logger.Warning($"  - Total devices in enumeration: {verifyDevices.Count}");
                    Logger.Warning("  - First 10 devices in enumeration:");
                    foreach (var dev in verifyDevices.Take(10))
                    {
                        Logger.Warning($"    - {dev.Name}");
                    }
                }
                
                Logger.Info($"Virtual MIDI port created successfully for '{deviceName}'");
                return port;
            }
            catch (Exception ex)
            {
                Logger.Error($"[EXCEPTION] Exception in CreateVirtualMidiPort for '{deviceName}': {ex.Message}");
                Logger.LogDebug($"[STACK TRACE] Exception details: {ex}");
                return null;
            }
        }
        
         public void RemoveVirtualMidiPort(ulong bluetoothAddress)
         {
             if (_virtualMidiPorts.TryGetValue(bluetoothAddress, out var port))
             {
                 Logger.Info($"Removing virtual MIDI port for device {bluetoothAddress:X}");
                 port?.Dispose();
                 _virtualMidiPorts.Remove(bluetoothAddress);
                 Logger.Info($"Successfully removed virtual MIDI port for device {bluetoothAddress:X}");
             }
            else
            {
                Logger.LogDebug($"No virtual MIDI port found for device {bluetoothAddress:X} (may have been cleaned up already)");
            }
         }
        
         public IMidiOutPort? GetVirtualMidiPort(ulong bluetoothAddress)
         {
             if (_virtualMidiPorts.TryGetValue(bluetoothAddress, out var port))
             {
                 return port;
             }
             return null;
         }
        
        public MidiPort OpenMidiPort(string deviceId, string deviceName, GattCharacteristic midiCharacteristic)
        {
            if (_midiPorts.TryGetValue(deviceId, out var existingPort))
            {
                Logger.Info($"MIDI port already exists for {deviceName}, returning existing port");
                return existingPort;
            }
            
            Logger.Info($"Creating new MIDI port for {deviceName}");
            var port = new MidiPort(deviceId, deviceName, midiCharacteristic);
            _midiPorts[deviceId] = port;
            
            Logger.Info($"Opened MIDI port for {deviceName}");
            return port;
        }
        
        public void CloseMidiPort(string deviceId)
        {
            if (_midiPorts.TryGetValue(deviceId, out var port))
            {
                Logger.Info($"Closing MIDI port for {deviceId}");
                port.Dispose();
                _midiPorts.Remove(deviceId);
                Logger.Info($"Successfully closed MIDI port for {deviceId}");
            }
            else
            {
                Logger.LogDebug($"No MIDI port found for {deviceId} (may have been cleaned up already)");
            }
        }
        
         public void CloseAllPorts()
         {
             Logger.Info($"Closing all {_midiPorts.Count} MIDI ports");
             foreach (var port in _midiPorts.Values)
             {
                 port.Dispose();
             }
             _midiPorts.Clear();
             Logger.Info("All MIDI ports closed");
         }

         public async Task<bool> TestMidiLoopback()
         {
             try
             {
                 Logger.Info("Starting MIDI loopback test to Microsoft GS Wavetable Synth");
                 var selector = MidiOutPort.GetDeviceSelector();
                 var devices = await DeviceInformation.FindAllAsync(selector);
                 var synth = devices.FirstOrDefault(d => d.Name.Contains("Wavetable"));
                 if (synth != null)
                 {
                     Logger.Info($"Found MIDI synth: {synth.Name}");
                     var port = await MidiOutPort.FromIdAsync(synth.Id);
                     if (port != null)
                     {
                         Logger.Info("Successfully opened MIDI port, sending test note");
                         port.SendMessage(new MidiNoteOnMessage(0, 60, 127));
                         Logger.Info("MIDI loopback test successful - test note sent");
                         return true;
                     }
                     Logger.Error("Failed to open MIDI output port");
                 }
                 else
                 {
                     Logger.Error("Microsoft GS Wavetable Synth not found");
                 }
                 return false;
             }
             catch (Exception ex)
             {
                 Logger.Error($"MIDI loopback test failed: {ex.Message}");
                 Logger.LogDebug($"Exception details: {ex}");
                 return false;
             }
         }
         
         /// <summary>
         /// Shuts down the MidiManager, closing all MIDI ports and virtual MIDI ports.
         /// Should be called when the application is exiting.
         /// </summary>
         public void Shutdown()
         {
             Logger.Info("=== MidiManager.Shutdown() starting ===");
             
             try
             {
                 // Close all GATT MIDI ports
                 Logger.Info($"Closing {_midiPorts.Count} GATT MIDI ports...");
                 CloseAllPorts();
                 
                 // Close and dispose all virtual MIDI ports (Windows MIDI API)
                 Logger.Info($"Disposing {_virtualMidiPorts.Count} Windows virtual MIDI ports...");
                 foreach (var kvp in _virtualMidiPorts.ToList())
                 {
                     try
                     {
                         Logger.Info($"Disposing virtual MIDI port for device 0x{kvp.Key:X}");
                         kvp.Value?.Dispose();
                     }
                     catch (Exception ex)
                     {
                         Logger.Warning($"Error disposing virtual MIDI port 0x{kvp.Key:X}: {ex.Message}");
                     }
                 }
                 _virtualMidiPorts.Clear();
                 
                 // Close and dispose all teVirtualMIDI ports
                 Logger.Info($"Disposing {_teVirtualMidiPorts.Count} teVirtualMIDI ports...");
                 foreach (var kvp in _teVirtualMidiPorts.ToList())
                 {
                     try
                     {
                         Logger.Info($"Disposing teVirtualMIDI port for device 0x{kvp.Key:X}");
                         kvp.Value?.Close();
                         kvp.Value?.Dispose();
                     }
                     catch (Exception ex)
                     {
                         Logger.Warning($"Error disposing teVirtualMIDI port 0x{kvp.Key:X}: {ex.Message}");
                     }
                 }
                 _teVirtualMidiPorts.Clear();
                 
                 Logger.Info("=== MidiManager.Shutdown() completed ===");
             }
             catch (Exception ex)
             {
                 Logger.Error($"Error during MidiManager shutdown: {ex.Message}");
                 Logger.LogDebug($"Shutdown exception details: {ex}");
             }
         }
     }
    
    public class MidiPort : IDisposable
    {
        private readonly GattCharacteristic _midiCharacteristic;
        private bool _disposed;
        
        public string DeviceId { get; }
        public string DeviceName { get; }
        public bool IsOpen { get; private set; }
        
        public MidiPort(string deviceId, string deviceName, GattCharacteristic midiCharacteristic)
        {
            DeviceId = deviceId;
            DeviceName = deviceName;
            _midiCharacteristic = midiCharacteristic;
            
            EnableNotifications();
            IsOpen = true;
        }
        
        private async void EnableNotifications()
        {
            try
            {
                Logger.Info($"Enabling MIDI notifications for {DeviceName}");
                var result = await _midiCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                
                if (result == GattCommunicationStatus.Success)
                {
                    Logger.Info($"MIDI notifications enabled for {DeviceName}");
                    _midiCharacteristic.ValueChanged += OnMidiDataReceived;
                }
                else
                {
                    Logger.Error($"Failed to enable MIDI notifications: {result}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error enabling notifications: {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
            }
        }
        
          // MINIMAL BLE MIDI PARSING IMPLEMENTATION
          // This parser assumes a simplified BLE MIDI structure where each message is exactly 5 bytes:
          // [timestampHigh][timestampLow][status][data1][data2]
          // Multiple messages can arrive in a single packet (length must be divisible by 5).
          // This does NOT handle running status, SysEx, or variable-length messages per the full BLE MIDI spec.
          private void OnMidiDataReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
          {
              var data = new byte[args.CharacteristicValue.Length];
              using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue))
              {
                  reader.ReadBytes(data);
              }
              
              const int BLE_MIDI_MESSAGE_SIZE = 5; // 2 timestamp bytes + 3 MIDI message bytes
              
              if (data.Length == 0)
              {
                  return;
              }
              
              if (data.Length % BLE_MIDI_MESSAGE_SIZE != 0)
              {
                  Logger.Warning($"[BLE MIDI] Received data length {data.Length} is not divisible by {BLE_MIDI_MESSAGE_SIZE}. " +
                      $"This minimal parser expects exactly {BLE_MIDI_MESSAGE_SIZE}-byte messages. Data may be malformed or use unsupported BLE MIDI features.");
              }
              
              var deviceId = ParseBluetoothAddressFromDeviceId(DeviceId);
              if (!deviceId.HasValue)
              {
                  return;
              }
              
              var virtualPort = MidiManager.Instance.GetTeVirtualMidiPort(deviceId.Value);
              var deviceInfo = MidiManager.Instance.GetDeviceInfo(deviceId.Value);
              
              // Process each 5-byte BLE MIDI message
              int messageCount = data.Length / BLE_MIDI_MESSAGE_SIZE;
              for (int i = 0; i < messageCount; i++)
              {
                  int offset = i * BLE_MIDI_MESSAGE_SIZE;
                  // Skip timestamp bytes (offset+0 and offset+1), extract 3-byte MIDI message
                  var midiMessage = new byte[3];
                  midiMessage[0] = data[offset + 2]; // status byte
                  midiMessage[1] = data[offset + 3]; // data1
                  midiMessage[2] = data[offset + 4]; // data2
                  
                  virtualPort?.SendData(midiMessage);
              }
              
              // Signal incoming traffic (once per packet, not per message)
              if (messageCount > 0)
              {
                  deviceInfo?.SetTraffic(isIncoming: true);
              }
          }
        
        private static ulong? ParseBluetoothAddressFromDeviceId(string deviceId)
         {
             try
             {
                 Logger.LogDebug($"Parsing Bluetooth address from DeviceId: {deviceId}");
                 
                 if (deviceId.StartsWith("BluetoothLE#"))
                 {
                     var afterPrefix = deviceId.Substring(12);
                     var dashIndex = afterPrefix.IndexOf('-');
                     
                     if (dashIndex >= 0)
                     {
                         afterPrefix = afterPrefix.Substring(dashIndex + 1);
                     }
                     
                     afterPrefix = afterPrefix.Replace(":", "").Replace("-", "");
                     
                     if (ulong.TryParse(afterPrefix, System.Globalization.NumberStyles.HexNumber, null, out var address))
                     {
                         Logger.LogDebug($"Successfully parsed Bluetooth address: 0x{address:X}");
                         return address;
                     }
                 }
                 
                 var parts = deviceId.Split('_');
                 if (parts.Length >= 2)
                 {
                     var hex = parts[1];
                     if (ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var address))
                     {
                         Logger.LogDebug($"Successfully parsed Bluetooth address (legacy format): 0x{address:X}");
                         return address;
                     }
                 }
                 
                 Logger.Error($"Failed to parse Bluetooth address from device ID '{deviceId}' - unrecognized format");
             }
             catch (Exception ex)
             {
                 Logger.Error($"Failed to parse Bluetooth address from device ID '{deviceId}': {ex.Message}");
                 Logger.LogDebug($"Exception details: {ex}");
             }
             return null;
         }
        
        public async void SendMidiData(byte[] data)
        {
            if (!_disposed && _midiCharacteristic != null)
            {
                try
                {
                    Logger.LogDebug($"Sending MIDI data ({data.Length} bytes): {BitConverter.ToString(data)}");
                    var writer = new Windows.Storage.Streams.DataWriter();
                    writer.WriteBytes(data);
                    
                    var result = await _midiCharacteristic.WriteValueAsync(
                        writer.DetachBuffer(), 
                        GattWriteOption.WriteWithoutResponse);
                    
                    if (result == GattCommunicationStatus.Success)
                    {
                        Logger.LogDebug($"MIDI data sent successfully");
                    }
                    else
                    {
                        Logger.Error($"Failed to send MIDI data: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error sending MIDI data: {ex.Message}");
                }
            }
            else
            {
                Logger.Warning($"Cannot send MIDI data: disposed={_disposed}, characteristic null={_midiCharacteristic == null}");
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Logger.Info($"Disposing MIDI port for {DeviceName}");
                _disposed = true;
                if (_midiCharacteristic != null)
                {
                    Logger.Info("Removing MIDI data event handler");
                    _midiCharacteristic.ValueChanged -= OnMidiDataReceived;
                }
                IsOpen = false;
                Logger.Info($"MIDI port disposed for {DeviceName}");
            }
        }
    }
}
