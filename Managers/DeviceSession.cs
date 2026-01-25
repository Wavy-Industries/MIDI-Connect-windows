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
        private readonly ulong _bluetoothAddress;
        private readonly string _deviceName;
        private string? _cachedDeviceId;
        
        public string Id => _cachedDeviceId ?? Device?.DeviceId ?? $"ble_{_bluetoothAddress:X}";
        public BluetoothLEDevice? Device { get; private set; }
        public string Name => Device?.Name ?? _deviceName;
        public ulong BluetoothAddress => _bluetoothAddress;
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
        
        public event Action<ulong>? Disconnected;
        
        public DeviceSession(BluetoothLEDevice device)
        {
            Device = device;
            _bluetoothAddress = device.BluetoothAddress;
            _deviceName = device.Name ?? "Unknown";
            IsConnected = false;
            IsConnecting = false;
            IsDisposed = false;
            _connectionTimestamp = DateTime.MinValue;
            _disconnectionTimestamp = DateTime.MinValue;
             
            Logger.Info($"Setting up ConnectionStatusChanged handler for {_deviceName}");
            Device.ConnectionStatusChanged += OnConnectionStatusChanged;
            Logger.LogDebug($"ConnectionStatusChanged handler attached to {_deviceName}");
        }
        
        private async void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            try
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
                    Logger.Info($"Device {sender.Name} disconnected unexpectedly");
                    _disconnectionTimestamp = DateTime.Now;
                    
                    // Clean up resources when device disconnects unexpectedly
                    if (IsConnected || IsConnecting)
                    {
                        IsConnected = false;
                        IsConnecting = false;
                        await CleanupResourcesAsync();
                        
                        // Notify listeners that device has disconnected
                        Disconnected?.Invoke(BluetoothAddress);
                    }
                    
                    Logger.Info($"Device {sender.Name} cleanup completed at {_disconnectionTimestamp}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in connection status handler for {sender?.Name}: {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
            }
        }
        
        private async Task CleanupResourcesAsync()
        {
            try
            {
                Logger.Info($"Cleaning up resources for {Name}");
                
                // First, disable GATT notifications if characteristic exists
                if (MidiCharacteristic != null)
                {
                    try
                    {
                        Logger.Info("Disabling MIDI notifications before cleanup");
                        await MidiCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Could not disable notifications (may already be disconnected): {ex.Message}");
                    }
                    MidiCharacteristic = null;
                }
                
                // Close MIDI GATT port
                if (_midiGattPort != null)
                {
                    Logger.Info($"Closing MIDI GATT port for {Name}");
                    MidiManager.Instance.CloseMidiPort(Id);
                    _midiGattPort = null;
                }
                
                // Close and dispose virtual MIDI port
                if (_midiPort != null)
                {
                    Logger.Info($"Disposing virtual MIDI port for {Name}");
                    MidiManager.Instance.UnregisterVirtualMidiPort(BluetoothAddress);
                    try
                    {
                        _midiPort.Close();
                        _midiPort.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Error disposing virtual MIDI port: {ex.Message}");
                    }
                    _midiPort = null;
                }
                
                Logger.Info($"Resource cleanup completed for {Name}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during resource cleanup for {Name}: {ex.Message}");
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
            
            Logger.Info($"=== Connection attempt for {Name} (0x{_bluetoothAddress:X}) ===");
            
            // Always get a fresh BluetoothLEDevice to avoid AccessDenied errors
            // The previous device reference may be stale or disposed
            if (Device != null)
            {
                Logger.Info("Disposing old device reference before getting fresh one");
                try
                {
                    Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    Device.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogDebug($"Error disposing old device: {ex.Message}");
                }
                Device = null;
            }
            
            Logger.Info($"Getting fresh BluetoothLEDevice from address: 0x{_bluetoothAddress:X}");
            var freshDevice = await GetDeviceFromAddress(_bluetoothAddress);
            if (freshDevice == null)
            {
                Logger.Error("Failed to get fresh device from address");
                return;
            }
            
            Device = freshDevice;
            Device.ConnectionStatusChanged += OnConnectionStatusChanged;
            IsDisposed = false;
            _cachedDeviceId = Device.DeviceId; // Cache the device ID for cleanup
            Logger.Info($"Fresh device obtained: {Device.Name} (ID: {_cachedDeviceId})");
                
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

                         // Detect MIDI direction from characteristic properties
                         var charProps = midiCharacteristic.CharacteristicProperties;
                         bool canSend = charProps.HasFlag(GattCharacteristicProperties.Notify) || 
                                        charProps.HasFlag(GattCharacteristicProperties.Indicate);
                         bool canReceive = charProps.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) || 
                                           charProps.HasFlag(GattCharacteristicProperties.Write);

                         uint portFlags;
                         if (canSend && canReceive)
                         {
                             portFlags = VirtualMidiPort.INSTANTIATE_BOTH;
                             Logger.Info($"Device {Name} supports bidirectional MIDI (Send + Receive)");
                         }
                         else if (canSend)
                         {
                             portFlags = VirtualMidiPort.INSTANTIATE_TX;
                             Logger.Info($"Device {Name} supports MIDI Send only (TX)");
                         }
                         else if (canReceive)
                         {
                             portFlags = VirtualMidiPort.INSTANTIATE_RX;
                             Logger.Info($"Device {Name} supports MIDI Receive only (RX)");
                         }
                         else
                         {
                             Logger.Warning($"Device {Name} has unexpected characteristic properties: {charProps}");
                             portFlags = VirtualMidiPort.INSTANTIATE_BOTH; // Fallback
                         }

                         Logger.Info($"Creating VirtualMidiPort with flags: {portFlags} (props: {charProps})");
                         if (virtualPort.Create(VirtualMidiPort.MAX_SYSEX_SIZE, portFlags))
                         {
                             Logger.Info($"VirtualMidiPort.Create() returned TRUE for {Name}");
                              virtualPort.DataReceived += async (sender, midiData) =>
                              {
                                  if (_midiGattPort.IsOpen && midiData.Length > 0)
                                  {
                                       var timestamp = MidiManager.Instance.GetNextTimestamp();
                                       // BLE MIDI spec requires bit 7 set on both timestamp bytes
                                       var timestampHigh = (byte)(0x80 | ((timestamp >> 7) & 0x3F));
                                       var timestampLow = (byte)(0x80 | (timestamp & 0x7F));
                                       var timestampBytes = new byte[] { timestampHigh, timestampLow };
                                      
                                      var bleData = new byte[timestampBytes.Length + midiData.Length];
                                      System.Buffer.BlockCopy(timestampBytes, 0, bleData, 0, 2);
                                      System.Buffer.BlockCopy(midiData, 0, bleData, 2, midiData.Length);
                                      
                                      Logger.LogDebug($"Sending to BLE - MIDI data ({midiData.Length} bytes): {BitConverter.ToString(midiData)}, Timestamp: 0x{timestampHigh:X2}{timestampLow:X2}");
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
                                      
                                      // Signal outgoing traffic  
                                      var deviceInfo = MidiManager.Instance.GetDeviceInfo(BluetoothAddress);
                                      deviceInfo?.SetTraffic(isIncoming: false);
                                  }
                              };
                             
                             // Only start receive task if device can receive MIDI from apps
                             if (canReceive)
                             {
                                 virtualPort.StartReceive();
                             }
                             else
                             {
                                 Logger.Info($"Skipping receive task for {Name} - device does not receive MIDI");
                             }
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
                        Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                        Device.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error disposing BLE device: {ex.Message}");
                    }
                    Device = null;
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
            if (IsDisposed)
            {
                Logger.Warning("Dispose called on already disposed DeviceSession");
                return;
            }
            
            IsDisposed = true;
            Logger.Info($"Disposing DeviceSession for {Name}");
            
            _disconnectionTimestamp = DateTime.Now;
            if (_connectionTimestamp != DateTime.MinValue)
            {
                var duration = _disconnectionTimestamp - _connectionTimestamp;
                Logger.Info($"Connection duration: {duration.TotalSeconds:F2} seconds");
            }
            
            // Unsubscribe from events first
            if (Device != null)
            {
                Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }
            
            // Disable notifications and clean up MIDI characteristic
            if (MidiCharacteristic != null)
            {
                try
                {
                    Logger.Info("Disabling MIDI notifications...");
                    // Fire and forget - we're disposing anyway
                    _ = MidiCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch (Exception ex)
                {
                    Logger.LogDebug($"Could not disable notifications during dispose: {ex.Message}");
                }
                MidiCharacteristic = null;
            }
            
            // Clean up virtual MIDI port (must be done before closing GATT port)
            if (_midiPort != null)
            {
                Logger.Info($"Disposing virtual MIDI port for {Name}");
                MidiManager.Instance.UnregisterVirtualMidiPort(BluetoothAddress);
                try
                {
                    _midiPort.Close();
                    _midiPort.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error disposing virtual MIDI port: {ex.Message}");
                }
                _midiPort = null;
            }
            
            // Clean up GATT port
            if (_midiGattPort != null)
            {
                Logger.Info($"Closing MIDI GATT port for {Name}");
                try
                {
                    MidiManager.Instance.CloseMidiPort(Id);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error closing MIDI GATT port: {ex.Message}");
                }
                _midiGattPort = null;
            }
            
            // Dispose BLE device last
            if (Device != null)
            {
                Logger.Info($"Disposing BluetoothLEDevice: {Device.Name}");
                try
                {
                    Device.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error disposing BLE device: {ex.Message}");
                }
                Device = null;
            }
            
            IsConnected = false;
            IsConnecting = false;
            
            Logger.Info($"DeviceSession disposed");
        }
    }
}
