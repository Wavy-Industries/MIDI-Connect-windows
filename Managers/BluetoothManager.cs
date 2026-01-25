using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace MinimalWindowsApp.Managers
{
    public class BluetoothManager
    {
        private static BluetoothManager? _instance;
        public static BluetoothManager Instance => _instance ??= new BluetoothManager();
        
        private readonly Dictionary<ulong, BluetoothDeviceInfo> _discoveredDevices = new();
        private readonly Dictionary<ulong, DeviceSession> _sessions = new();
        private readonly BluetoothLEAdvertisementWatcher _advertisementWatcher;
        
        public event Action<BluetoothDeviceInfo>? DeviceDiscovered;
        public event Action<ulong>? DeviceDisconnected;
        
        private BluetoothManager()
        {
            _advertisementWatcher = new BluetoothLEAdvertisementWatcher();
            
            // Configure for active scanning (more thorough)
            _advertisementWatcher.ScanningMode = BluetoothLEScanningMode.Active;
            
            // Set signal strength filter to be more permissive
            _advertisementWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -120;
            _advertisementWatcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -127;
            _advertisementWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromSeconds(30);
            _advertisementWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromSeconds(1);
            
            // Filter for MIDI service UUID
            var midiServiceUuid = new Guid("03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
            _advertisementWatcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(midiServiceUuid);
            
            _advertisementWatcher.Received += OnAdvertisementReceived;
            _advertisementWatcher.Stopped += OnAdvertisementWatcherStopped;
        }
        
        public void StartScanning()
        {
            Logger.Info("Starting Bluetooth device scan...");
            _advertisementWatcher.Start();
            Logger.Info("Bluetooth scanning started");
        }
        
        public void StopScanning()
        {
            _advertisementWatcher.Stop();
            Logger.Info("Stopped Bluetooth LE MIDI device scanning");
        }
        
        public async Task<DeviceSession?> GetOrCreateSession(ulong bluetoothAddress)
        {
            if (_sessions.TryGetValue(bluetoothAddress, out var existingSession))
            {
                if (!existingSession.IsDisposed)
                {
                    Logger.LogDebug($"Returning existing session for 0x{bluetoothAddress:X}");
                    return existingSession;
                }
                
                Logger.Info($"Session for 0x{bluetoothAddress:X} was disposed, removing and creating new session");
                _sessions.Remove(bluetoothAddress);
            }
            
            Logger.Info($"Creating new DeviceSession from address: 0x{bluetoothAddress:X}");
            var newSession = await DeviceSession.CreateFromAddress(bluetoothAddress);
            if (newSession != null)
            {
                newSession.Disconnected += OnSessionDisconnected;
                _sessions[bluetoothAddress] = newSession;
                Logger.Info($"New session created and stored for 0x{bluetoothAddress:X}");
            }
            
            return newSession;
        }
        
        public void RemoveSession(ulong bluetoothAddress)
        {
            if (_sessions.ContainsKey(bluetoothAddress))
            {
                Logger.Info($"Removing session for 0x{bluetoothAddress:X}");
                _sessions.Remove(bluetoothAddress);
                DeviceDisconnected?.Invoke(bluetoothAddress);
            }
        }
        
        private void OnSessionDisconnected(ulong bluetoothAddress)
        {
            Logger.Info($"Session disconnected event received for 0x{bluetoothAddress:X}");
            if (_sessions.ContainsKey(bluetoothAddress))
            {
                _sessions.Remove(bluetoothAddress);
            }
            DeviceDisconnected?.Invoke(bluetoothAddress);
        }
        
        public BluetoothDeviceInfo? GetDeviceInfo(ulong bluetoothAddress)
        {
            _discoveredDevices.TryGetValue(bluetoothAddress, out var deviceInfo);
            return deviceInfo;
        }
        
        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                var deviceName = args.Advertisement.LocalName ?? "Unknown";
                Logger.Info($"BLE advertisement from {deviceName} (RSSI: {args.RawSignalStrengthInDBm} dBm)");
                
                var hasMidiService = args.Advertisement.ServiceUuids.Any(uuid => 
                    uuid.ToString().ToUpper() == "03B80E5A-EDE8-4B33-A751-6CE34EC4C700");
                
                if (!hasMidiService)
                {
                    Logger.LogDebug($"  -> No MIDI service found in advertisement");
                    return;
                }
                
                Logger.Info($"  -> MIDI SERVICE DETECTED!");
                
                if (_discoveredDevices.ContainsKey(args.BluetoothAddress))
                {
                    Logger.LogDebug($"  -> Device already discovered, skipping");
                    return;
                }
                
                _ = TryGetDeviceNameAndCreateInfo(args.BluetoothAddress);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to process MIDI device advertisement: {ex.Message}");
            }
        }
        
        private async Task TryGetDeviceNameAndCreateInfo(ulong bluetoothAddress)
        {
            try
            {
                Logger.LogDebug($"Getting device info for address: 0x{bluetoothAddress:X}");
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (device != null)
                {
                    var actualName = device.Name ?? "Unknown";
                    Logger.Info($"Device discovered: {actualName} (ConnectionStatus: {device.ConnectionStatus})");
                    
                    var deviceInfo = new BluetoothDeviceInfo(bluetoothAddress, actualName, device);
                    _discoveredDevices[bluetoothAddress] = deviceInfo;
                    DeviceDiscovered?.Invoke(deviceInfo);
                }
                else
                {
                    Logger.Warning($"Device from address 0x{bluetoothAddress:X} is null");
                    var deviceInfo = new BluetoothDeviceInfo(bluetoothAddress, "Unknown", null);
                    _discoveredDevices[bluetoothAddress] = deviceInfo;
                    DeviceDiscovered?.Invoke(deviceInfo);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get device from address 0x{bluetoothAddress:X}: {ex.Message}");
                var deviceInfo = new BluetoothDeviceInfo(bluetoothAddress, "Unknown", null);
                _discoveredDevices[bluetoothAddress] = deviceInfo;
                DeviceDiscovered?.Invoke(deviceInfo);
            }
        }
        
        private void OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Logger.Warning($"Advertisement watcher stopped: {args.Error}");
        }
    }
    
    public class BluetoothDeviceInfo : INotifyPropertyChanged
    {
        private bool _incomingTraffic;
        private bool _outgoingTraffic;
        private System.Windows.Threading.DispatcherTimer? _trafficClearTimer;
        
        public ulong BluetoothAddress { get; }
        public string Name { get; }
        public BluetoothLEDevice? Device { get; }
        
        public bool IncomingTraffic
        {
            get => _incomingTraffic;
            private set
            {
                if (_incomingTraffic != value)
                {
                    _incomingTraffic = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool OutgoingTraffic
        {
            get => _outgoingTraffic;
            private set
            {
                if (_outgoingTraffic != value)
                {
                    _outgoingTraffic = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public BluetoothDeviceInfo(ulong bluetoothAddress, string name, BluetoothLEDevice? device)
        {
            BluetoothAddress = bluetoothAddress;
            Name = name;
            Device = device;
        }
        
        public void SetTraffic(bool isIncoming)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (isIncoming)
                    IncomingTraffic = true;
                else
                    OutgoingTraffic = true;
                
                ResetTrafficClearTimer();
            });
        }
        
        private void ResetTrafficClearTimer()
        {
            if (_trafficClearTimer == null)
            {
                _trafficClearTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _trafficClearTimer.Tick += (s, e) =>
                {
                    IncomingTraffic = false;
                    OutgoingTraffic = false;
                    _trafficClearTimer.Stop();
                };
            }
            
            _trafficClearTimer.Stop();
            _trafficClearTimer.Start();
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}