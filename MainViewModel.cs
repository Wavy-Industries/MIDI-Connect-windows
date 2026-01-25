using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MinimalWindowsApp.Managers;

namespace MinimalWindowsApp
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly BluetoothManager _bluetoothManager;
        private readonly MidiManager _midiManager;
        private string _status = "Initializing...";
        private readonly Dictionary<ulong, DeviceSession> _deviceSessions = new();
        private BluetoothDeviceInfo? _selectedConnectedDevice;
        private BluetoothDeviceInfo? _selectedDiscoveredDevice;
        
        public ObservableCollection<BluetoothDeviceInfo> DiscoveredDevices { get; } = new();
        public ObservableCollection<BluetoothDeviceInfo> ConnectedDevices { get; } = new();
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand TestMidiCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand ExitCommand { get; }
        
        public BluetoothDeviceInfo? SelectedConnectedDevice
        {
            get => _selectedConnectedDevice;
            set
            {
                _selectedConnectedDevice = value;
                OnPropertyChanged();
            }
        }
        
        public BluetoothDeviceInfo? SelectedDiscoveredDevice
        {
            get => _selectedDiscoveredDevice;
            set
            {
                _selectedDiscoveredDevice = value;
                OnPropertyChanged();
            }
        }
        
        public bool HasAnyDevices => ConnectedDevices.Count > 0 || DiscoveredDevices.Count > 0;
        
        public bool HasConnectedDevices => ConnectedDevices.Count > 0;
        public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
        
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }
        
        public MainViewModel()
        {
            DiscoveredDevices.CollectionChanged += (_, e) => 
            {
                Logger.Info($"DiscoveredDevices collection changed: Action={e.Action}, Count={DiscoveredDevices.Count}");
                OnPropertyChanged(nameof(HasAnyDevices));
                OnPropertyChanged(nameof(HasDiscoveredDevices));
            };
            ConnectedDevices.CollectionChanged += (_, e) => 
            {
                Logger.Info($"ConnectedDevices collection changed: Action={e.Action}, Count={ConnectedDevices.Count}");
                OnPropertyChanged(nameof(HasAnyDevices));
                OnPropertyChanged(nameof(HasConnectedDevices));
            };
            try
            {
                _bluetoothManager = BluetoothManager.Instance;
                _midiManager = MidiManager.Instance;
                
                ExitCommand = new RelayCommand<object>(_ => Application.Current.Shutdown());
                
                _bluetoothManager.DeviceDiscovered += OnDeviceDiscovered;
                _bluetoothManager.DeviceDisconnected += OnDeviceDisconnected;
                
                ConnectCommand = new RelayCommand<BluetoothDeviceInfo>(async device => await ConnectToDevice(device));
                DisconnectCommand = new RelayCommand<BluetoothDeviceInfo>(DisconnectDevice);
                TestMidiCommand = new RelayCommand<object>(async _ => await TestMidiLoopback());
                SettingsCommand = new RelayCommand<BluetoothDeviceInfo>(ShowDeviceSettings);
                
                Status = "Starting Bluetooth scan...";
                _bluetoothManager.StartScanning();
                Status = "Scanning for MIDI devices...";
                
                Logger.Info($"MainViewModel initialized. DiscoveredDevices: {DiscoveredDevices.Count}, ConnectedDevices: {ConnectedDevices.Count}");
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                Logger.Error($"Failed to initialize: {ex.Message}");
                MessageBox.Show($"Failed to initialize: {ex.Message}\n\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void OnDeviceDiscovered(BluetoothDeviceInfo device)
        {
            Logger.Info($"Device discovered: {device.Name}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                DiscoveredDevices.Add(device);
                Status = $"Found device: {device.Name}";
            });
        }
        
        private void OnDeviceDisconnected(ulong bluetoothAddress)
        {
            Logger.Info($"Device disconnected event received for 0x{bluetoothAddress:X}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Find the device in connected devices
                BluetoothDeviceInfo? device = null;
                foreach (var d in ConnectedDevices)
                {
                    if (d.BluetoothAddress == bluetoothAddress)
                    {
                        device = d;
                        break;
                    }
                }
                
                if (device != null)
                {
                    Logger.Info($"Moving {device.Name} from Connected to Discovered");
                    ConnectedDevices.Remove(device);
                    
                    // Only add back to discovered if not already there
                    bool alreadyInDiscovered = false;
                    foreach (var d in DiscoveredDevices)
                    {
                        if (d.BluetoothAddress == bluetoothAddress)
                        {
                            alreadyInDiscovered = true;
                            break;
                        }
                    }
                    
                    if (!alreadyInDiscovered)
                    {
                        DiscoveredDevices.Add(device);
                    }
                    
                    Status = $"Disconnected from {device.Name}";
                    
                    // Clean up our session reference
                    _deviceSessions.Remove(bluetoothAddress);
                }
            });
        }
        
        private async Task ConnectToDevice(BluetoothDeviceInfo device)
        {
            try
            {
                Logger.Info($"Connecting to {device.Name}...");
                Status = $"Connecting to {device.Name}...";
                
                var session = await _bluetoothManager.GetOrCreateSession(device.BluetoothAddress);
                if (session == null)
                {
                    Status = "Failed to create device session";
                    Logger.Error($"Failed to create session for {device.Name}");
                    return;
                }
                
                await session.ConnectAsync();
                
                if (session.Device == null)
                {
                    Logger.Error("DeviceSession device is null after connection attempt");
                    Status = "Device connection failed";
                    return;
                }
                
                if (session.MidiCharacteristic is not null)
                {
                    _midiManager.OpenMidiPort(session.Device.DeviceId, device.Name, session.MidiCharacteristic);
                    _deviceSessions[device.BluetoothAddress] = session;
                    
                    Logger.Info($"Connected to {device.Name}. Current state - DiscoveredDevices: {DiscoveredDevices.Count}, ConnectedDevices: {ConnectedDevices.Count}");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Logger.Info($"Before: DiscoveredDevices={DiscoveredDevices.Count}, ConnectedDevices={ConnectedDevices.Count}");
                        DiscoveredDevices.Remove(device);
                        Logger.Info($"After Remove from DiscoveredDevices: Count={DiscoveredDevices.Count}");
                        ConnectedDevices.Add(device);
                        Logger.Info($"After Add to ConnectedDevices: Count={ConnectedDevices.Count}");
                        Status = $"Connected to {device.Name}";
                    });
                }
                else
                {
                    Logger.Error("Failed to find MIDI characteristic in connected device");
                    Status = "Failed to find MIDI service";
                    
                    if (session.IsConnected)
                    {
                        Logger.Info("Disconnecting session due to missing MIDI characteristic");
                        session.IsConnected = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Status = $"Connection failed: {ex.Message}";
                Logger.Error($"Failed to connect to {device.Name}: {ex.Message}");
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void DisconnectDevice(BluetoothDeviceInfo device)
        {
            if (device == null)
            {
                Logger.Warning("DisconnectDevice called with null device");
                return;
            }
            
            try
            {
                Logger.Info($"=== Disconnecting from {device.Name} ===");
                
                var deviceId = device.Device?.DeviceId ?? $"{device.BluetoothAddress}";
                Logger.Info($"Closing MIDI port for deviceId: {deviceId}");
                
                try
                {
                    _midiManager.CloseMidiPort(deviceId);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error closing MIDI port: {ex.Message}");
                }
                
                Logger.Info($"Disposing DeviceSession for {device.Name}");
                if (_deviceSessions.TryGetValue(device.BluetoothAddress, out var session))
                {
                    Logger.Info($"Session was connected: {session.IsConnected}");
                    Logger.Info($"Session was connecting: {session.IsConnecting}");
                    session.Dispose();
                    _deviceSessions.Remove(device.BluetoothAddress);
                }
                else
                {
                    Logger.Warning($"No session found for device {device.Name}");
                }
                
                _bluetoothManager.RemoveSession(device.BluetoothAddress);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Logger.Info($"Before disconnect: ConnectedDevices={ConnectedDevices.Count}, DiscoveredDevices={DiscoveredDevices.Count}");
                    ConnectedDevices.Remove(device);
                    Logger.Info($"After Remove from ConnectedDevices: Count={ConnectedDevices.Count}");
                    
                    // Only add back to discovered if not already there
                    bool alreadyInDiscovered = false;
                    foreach (var d in DiscoveredDevices)
                    {
                        if (d.BluetoothAddress == device.BluetoothAddress)
                        {
                            alreadyInDiscovered = true;
                            break;
                        }
                    }
                    
                    if (!alreadyInDiscovered)
                    {
                        DiscoveredDevices.Add(device);
                        Logger.Info($"After Add to DiscoveredDevices: Count={DiscoveredDevices.Count}");
                    }
                    else
                    {
                        Logger.Info($"Device already in DiscoveredDevices, not adding duplicate");
                    }
                    
                    Status = $"Disconnected from {device.Name}";
                });
                
                Logger.Info($"=== Successfully disconnected from {device.Name} ===");
            }
            catch (Exception ex)
            {
                Status = $"Disconnect failed: {ex.Message}";
                Logger.Error($"Failed to disconnect from {device.Name}: {ex.Message}");
                Logger.LogDebug($"Disconnect exception details: {ex}");
            }
        }
        
        private void ShowDeviceSettings(BluetoothDeviceInfo device)
        {
            Status = $"Settings for {device.Name}";
        }
        
        private async Task TestMidiLoopback()
        {
            Status = "Testing MIDI loopback...";
            bool success = await _midiManager.TestMidiLoopback();
            if (success)
            {
                Status = "MIDI loopback test successful";
                MessageBox.Show("MIDI loopback test successful!", "Test Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Status = "MIDI loopback test failed";
                MessageBox.Show("MIDI loopback test failed. Check logs for details.", "Test Result", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void Dispose()
        {
            Logger.Info("Disposing MainViewModel");
            
            // Unsubscribe from events
            _bluetoothManager.DeviceDiscovered -= OnDeviceDiscovered;
            _bluetoothManager.DeviceDisconnected -= OnDeviceDisconnected;
            
            // Dispose all active sessions
            foreach (var session in _deviceSessions.Values)
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error disposing session: {ex.Message}");
                }
            }
            _deviceSessions.Clear();
            
            // Stop bluetooth scanning
            _bluetoothManager.StopScanning();
            
            Logger.Info("MainViewModel disposed");
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T>? _canExecute;
        
        public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        public bool CanExecute(object? parameter)
        {
            if (parameter == null && default(T) != null)
                return _canExecute == null;
            return _canExecute?.Invoke((T)parameter!) ?? true;
        }

        public void Execute(object? parameter)
        {
            if (parameter == null && default(T) != null)
                return;
            _execute((T)parameter!);
        }
        public event EventHandler? CanExecuteChanged {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
