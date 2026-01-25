using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MinimalWindowsApp.Managers
{
    public class DeviceSession : IDisposable
    {
        public string Id => Device?.DeviceId ?? "unknown";
        public BluetoothLEDevice Device { get; private set; }
        public string Name => Device?.Name ?? "Unknown";
        public ulong BluetoothAddress => Device?.BluetoothAddress ?? 0;
        public bool IsDisposed { get; private set; }
        
        public bool IsConnected { get; set; }
        public bool IsConnecting { get; set; }
        
         public GattCharacteristic? MidiCharacteristic { get; set; }
         
         private VirtualMidiPort? _midiPort;
         private MidiPort? _midiGattPort;
         private DateTime _connectionTimestamp;
         private DateTime _disconnectionTimestamp;
         private readonly object _cleanupLock = new object();
        
        public DateTime ConnectionTimestamp => _connectionTimestamp;
        public DateTime DisconnectionTimestamp => _disconnectionTimestamp;
        
         public DeviceSession(BluetoothLEDevice device)
         {
             Device = device;
             IsConnected = false;
             IsConnecting = false;
             IsDisposed = false;
             _connectionTimestamp = DateTime.MinValue;
             _disconnectionTimestamp = DateTime.MinValue;
             
             Logger.Info($"Setting up ConnectionStatusChanged handler for {device.Name}");
             Device.ConnectionStatusChanged += OnConnectionStatusChanged;
             Logger.LogDebug($"ConnectionStatusChanged handler attached to {device.Name}");
         }
        
        private async void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            Logger.Info($"Connection status changed for {sender.Name}: {sender.ConnectionStatus}");
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                IsConnected = true;
                IsConnecting = false;
                _connectionTimestamp = DateTime.Now;
                Logger.Info($"Device {sender.Name} connected at {_connectionTimestamp}");
            }
            else if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                IsConnected = false;
                IsConnecting = false;
                _disconnectionTimestamp = DateTime.Now;
                Logger.Info($"Device {sender.Name} disconnected at {_disconnectionTimestamp}");
            }
        }
        
        public static async Task<DeviceSession?> CreateFromAddress(ulong bluetoothAddress)
        {
            Logger.Info($"Creating DeviceSession from bluetooth address: 0x{bluetoothAddress:X}");
            var device = await GetDeviceFromAddress(bluetoothAddress);
            if (device != null)
            {
                return new DeviceSession(device);
            }
            return null;
        }
        
        public static async Task<BluetoothLEDevice?> GetDeviceFromAddress(ulong bluetoothAddress)
        {
            try
            {
                return await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get device from address: {ex.Message}");
                return null;
            }
        }
        
        public async Task ConnectAsync()
        {
                        if (IsConnected || IsConnecting)
                        {
                            Logger.Warning($"ConnectAsync called but connection in progress or already connected");
                            return;
                        }
                        
                        Logger.Info($"=== Connection attempt for {Name} (0x{BluetoothAddress:X}) ===");
                        
                        Logger.Info($"=== Connection attempt for {Name} (0x{BluetoothAddress:X}) ===");
            
            if (IsDisposed || Device == null)
            {
                if (BluetoothAddress == 0)
                {
                    Logger.Error("Cannot reconnect: BluetoothAddress is 0");
                    Logger.Warning("ConnectAsync returning early - BluetoothAddress is 0");
                    return;
                }
                
                Logger.Info($"DeviceSession was disposed, recreating from address: 0x{BluetoothAddress:X}");
                var device = await GetDeviceFromAddress(BluetoothAddress);
                if (device != null)
                {
                    Device = device;
                    IsDisposed = false;
                    Logger.Info("Device recreated successfully");
                }
                else
                {
                    Logger.Error("Failed to recreate device from address");
                    Logger.Warning("ConnectAsync returning early - failed to recreate device");
                    return;
                }
            }
                
            IsConnecting = true;
            _connectionTimestamp = DateTime.Now;
            
                try
                {
                    Logger.Info($"Starting GATT service discovery for {Name}...");
                    Logger.LogDebug($"BLE device ID: {Device.DeviceId}");
                    Logger.LogDebug($"BLE device connection status: {Device.ConnectionStatus}");
                    var gattServices = await Device.GetGattServicesAsync();
                
                if (gattServices.Status == GattCommunicationStatus.Success)
                {
                    Logger.Info($"Found {gattServices.Services.Count} services on {Name}");
                    
                    foreach (var service in gattServices.Services)
                    {
                        Logger.LogDebug($"  Service UUID: {service.Uuid}");
                    }
                    
                    var midiService = gattServices.Services.FirstOrDefault(service => 
                        service.Uuid.ToString().ToUpper() == "03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
                        
                    if (midiService != null)
                    {
                        Logger.Info($"MIDI service found on {Name}");
                        
                        Logger.Info($"Discovering characteristics for {Name}...");
                        var characteristics = await midiService.GetCharacteristicsAsync();
                        
                         if (characteristics.Status == GattCommunicationStatus.Success)
                         {
                             Logger.Info($"Found {characteristics.Characteristics.Count} characteristics");
                             
                             foreach (var characteristic in characteristics.Characteristics)
                             {
                                 Logger.LogDebug($"  Characteristic UUID: {characteristic.Uuid}");
                             }
                             
                      var midiCharacteristic = characteristics.Characteristics.FirstOrDefault(c => 
                          c.Uuid.ToString().ToUpper() == "7772E5DB-3868-4112-A1A9-F2669D106BF3");

                      if (midiCharacteristic == null)
                      {
                          Logger.Error("FAILED to find MIDI characteristic with UUID 7772E5DB-3868-4112-A1A9-F2669D106BF3");
                          Logger.Error($"Available characteristics:");
                          foreach (var characteristic in characteristics.Characteristics)
                          {
                              Logger.Error($"  - UUID: {characteristic.Uuid}");
                          }
                          return;
                      }

                      Logger.Info($"MIDI characteristic FOUND: {midiCharacteristic.Uuid}");
                      
                      Logger.Info($"Assigning MIDI characteristic to DeviceSession property");
                      MidiCharacteristic = midiCharacteristic;
                      
                      Logger.Info($"Creating MidiPort for {Name}");
                        _midiGattPort = MidiManager.Instance.OpenMidiPort(Device.DeviceId, Name, midiCharacteristic);
                      if (_midiGattPort != null && _midiGattPort.IsOpen)
                      {
                          Logger.Info($"MidiPort created and opened successfully for {Name}");
                          IsConnected = true;
                         Logger.Info($"Device connected successfully: {Name}");
                         Logger.Info($"Connection timestamp: {_connectionTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                           
                         Logger.Info($"Instantiating VirtualMidiPort for {Name}");
                         var virtualPort = new VirtualMidiPort(Name, BluetoothAddress);
                         
                         Logger.Info($"About to call Create() on VirtualMidiPort for {Name}");
                         if (virtualPort.Create())
                         {
                             Logger.Info($"VirtualMidiPort.Create() returned TRUE for {Name}");
                              virtualPort.DataReceived += async (sender, midiData) =>
                              {
                                  if (_midiGattPort.IsOpen && midiData.Length > 0)
                                  {
                                       var timestamp = MidiManager.Instance.GetNextTimestamp();
                                       var timestampBytes = new byte[] { (byte)((timestamp >> 7) & 0x7F), (byte)(timestamp & 0x7F) };
                                      
                                      var bleData = new byte[timestampBytes.Length + midiData.Length];
                                      System.Buffer.BlockCopy(timestampBytes, 0, bleData, 0, 2);
                                      System.Buffer.BlockCopy(midiData, 0, bleData, 2, midiData.Length);
                                      
                                      Logger.LogDebug($"Sending to BLE - MIDI data ({midiData.Length} bytes): {BitConverter.ToString(midiData)}, Timestamp: 0x{timestampBytes[0]:X2}{timestampBytes[1]:X2}");
                                      Logger.LogDebug($"BLE data ({bleData.Length} bytes): {BitConverter.ToString(bleData)}");
                                      
                                      try
                                      {
                                          _midiGattPort.SendMidiData(bleData);
                                          Logger.LogDebug($"MIDI data sent to BLE device {Name}");
                                      }
                                      catch (Exception ex)
                                      {
                                          Logger.Error($"Error sending MIDI data to BLE device {Name}: {ex.Message}");
                                      }
                                  }
                              };
                             
                             virtualPort.StartReceive();
                             MidiManager.Instance.RegisterVirtualMidiPort(BluetoothAddress, virtualPort);
                             
                             _midiPort = virtualPort;
                             Logger.Info($"Virtual MIDI port created successfully for {Name}");
                              }
                               else
                               {
                                   Logger.Error($"VirtualMidiPort.Create() returned FALSE for {Name}");
                                   Logger.Warning("ConnectAsync returning early - Create() failed");
                               }
                          }
                          else
                          {
                              Logger.Error($"MidiPort failed to open for {Name}");
                          }
                         }
                         else
                         {
                             Logger.Error($"Failed to get characteristics: {characteristics.Status}");
                         }
                    }
                    else
                    {
                        Logger.Error($"MIDI service not found on {Name}");
                    }
                }
                else
                {
                    Logger.Error($"Failed to get GATT services: {gattServices.Status}");
                }
            }
            catch (Exception ex)
            {
               Logger.Error($"Failed to connect: {ex.Message}");
               Logger.LogDebug($"Exception details: {ex}");
            }
            finally
            {
                IsConnecting = false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!IsConnected && !IsConnecting)
            {
                Logger.Warning($"Disconnect called, but {Name} is already disconnected");
                if (IsDisposed)
                {
                    Logger.Warning($"DeviceSession for {Name} is already disposed");
                }
                return;
            }
            
            Logger.Info($"=== Disconnecting from {Name} (0x{BluetoothAddress:X}) ===");
            IsConnecting = false;
            
            try
            {
                _disconnectionTimestamp = DateTime.Now;
                Logger.Info($"Disconnection timestamp: {_disconnectionTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
                
                if (_connectionTimestamp != DateTime.MinValue)
                {
                    var duration = _disconnectionTimestamp - _connectionTimestamp;
                    Logger.Info($"Connection duration: {duration.TotalSeconds:F2} seconds");
                }
                
                if (_midiGattPort != null)
                {
                    Logger.Info($"Closing MIDI GATT port for {Name}");
                    MidiManager.Instance.CloseMidiPort(Id);
                    _midiGattPort = null;
                }
                
                if (MidiCharacteristic != null)
                {
                    Logger.Info("Cleaning up MIDI characteristic reference");
                    MidiCharacteristic = null;
                }
                
                if (BluetoothAddress != 0)
                {
                    Logger.Info($"Removing virtual MIDI port from MidiManager for {Name}");
                    MidiManager.Instance.RemoveVirtualMidiPort(BluetoothAddress);
                    MidiManager.Instance.UnregisterVirtualMidiPort(BluetoothAddress);
                }
                
                if (_midiPort != null)
                {
                    Logger.Info($"Calling VirtualMidiPort.Close() for {Name}");
                    try
                    {
                        _midiPort.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error closing virtual MIDI port: {ex.Message}");
                    }
                    
                    Logger.Info($"Disposing VirtualMidiPort for {Name}");
                    try
                    {
                        _midiPort.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing virtual MIDI port: {ex.Message}");
                    }
                    _midiPort = null;
                }
                
                if (MidiCharacteristic != null)
                {
                    Logger.Info($"Cleaning up MIDI characteristic reference for {Name}");
                    MidiCharacteristic = null;
                }
                
                Logger.Info($"Cleaning up BLE device: {Device?.Name}");
                if (Device != null)
                {
                    try
                    {
                        Device.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing BLE device: {ex.Message}");
                    }
                    Device = null!;
                }
                
                IsConnected = false;
                Logger.Info($"=== Disconnect completed for {Name} ===");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during disconnect for {Name}: {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
                throw;
            }
        }
        
        public void Dispose()
        {
            if (IsDisposed || Device == null)
            {
                Logger.Warning("Dispose called on already disposed DeviceSession");
                return;
            }
            
            IsDisposed = true;
            Logger.Info($"Disposing DeviceSession for {Name}");
            
            _disconnectionTimestamp = DateTime.Now;
            Logger.Info($"Disconnection timestamp: {_disconnectionTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
            
            if (_connectionTimestamp != DateTime.MinValue)
            {
                var duration = _disconnectionTimestamp - _connectionTimestamp;
                Logger.Info($"Connection duration: {duration.TotalSeconds:F2} seconds");
            }
            
            if (MidiCharacteristic != null)
            {
                Logger.Info("Cleaning up MIDI characteristic...");
                MidiCharacteristic = null;
            }
            
            if (BluetoothAddress != 0)
            {
                Logger.Info($"Removing virtual MIDI port for {Name}");
                MidiManager.Instance.RemoveVirtualMidiPort(BluetoothAddress);
            }
            
            if (_midiGattPort != null)
            {
                Logger.Info($"Closing MIDI GATT port for {Name}");
                MidiManager.Instance.CloseMidiPort(Id);
                _midiGattPort = null;
            }
            
            if (_midiPort != null)
            {
                Logger.Info($"Disposing virtual MIDI port for {Name}");
                _midiPort.Dispose();
                _midiPort = null;
            }
            
            Logger.Info($"Disposing BluetoothLEDevice: {Device.Name}");
            Device.Dispose();
            Device = null!;
            
            IsConnected = false;
            IsConnecting = false;
            
            Logger.Info($"DeviceSession disposed");
        }
    }
}
