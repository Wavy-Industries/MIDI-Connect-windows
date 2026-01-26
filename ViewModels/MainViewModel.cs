using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MinimalWindowsApp.Infrastructure;
using MinimalWindowsApp.Services;

namespace MinimalWindowsApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly BluetoothService _bluetoothService;
        private string _status = "Initializing...";
        private DeviceSession? _selectedConnectedDevice;
        private DeviceSession? _selectedDiscoveredDevice;

        public ObservableCollection<DeviceSession> DiscoveredDevices { get; } = new();
        public ObservableCollection<DeviceSession> ConnectedDevices { get; } = new();
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand TestMidiCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand ExitCommand { get; }

        public DeviceSession? SelectedConnectedDevice
        {
            get => _selectedConnectedDevice;
            set
            {
                _selectedConnectedDevice = value;
                OnPropertyChanged();
            }
        }

        public DeviceSession? SelectedDiscoveredDevice
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
                _bluetoothService = BluetoothService.Instance;

                ExitCommand = new RelayCommand<object>(_ => Application.Current.Shutdown());

                _bluetoothService.DeviceDiscovered += OnDeviceDiscovered;
                _bluetoothService.DeviceDisconnected += OnDeviceDisconnected;

                ConnectCommand = new RelayCommand<DeviceSession>(async device => await ConnectToDevice(device));
                DisconnectCommand = new RelayCommand<DeviceSession>(DisconnectDevice);
                TestMidiCommand = new RelayCommand<object>(async _ => await TestMidiLoopback());
                SettingsCommand = new RelayCommand<DeviceSession>(ShowDeviceSettings);

                Status = "Starting Bluetooth scan...";
                _bluetoothService.StartScanning();
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

        private void OnDeviceDiscovered(DeviceSession device)
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
                DeviceSession? device = null;
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
                }
            });
        }

        private async Task ConnectToDevice(DeviceSession device)
        {
            try
            {
                Logger.Info($"Connecting to {device.Name}...");
                Status = $"Connecting to {device.Name}...";

                await device.ConnectAsync();

                if (device.Device == null)
                {
                    Logger.Error("DeviceSession device is null after connection attempt");
                    Status = "Device connection failed";
                    return;
                }

                if (device.MidiCharacteristic is not null)
                {
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

                    if (device.IsConnected)
                    {
                        Logger.Info("Disconnecting session due to missing MIDI characteristic");
                        device.IsConnected = false;
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

        private void DisconnectDevice(DeviceSession device)
        {
            if (device == null)
            {
                Logger.Warning("DisconnectDevice called with null device");
                return;
            }

            try
            {
                Logger.Info($"=== Disconnecting from {device.Name} ===");

                Logger.Info($"Disposing DeviceSession for {device.Name}");
                Logger.Info($"Session was connected: {device.IsConnected}");
                Logger.Info($"Session was connecting: {device.IsConnecting}");
                device.Dispose();

                _bluetoothService.RemoveSession(device.BluetoothAddress);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Logger.Info($"Before disconnect: ConnectedDevices={ConnectedDevices.Count}, DiscoveredDevices={DiscoveredDevices.Count}");
                    ConnectedDevices.Remove(device);
                    Logger.Info($"After Remove from ConnectedDevices: Count={ConnectedDevices.Count}");

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

        private void ShowDeviceSettings(DeviceSession device)
        {
            Status = $"Settings for {device.Name}";
        }

        private async Task TestMidiLoopback()
        {
            Status = "Testing MIDI loopback...";
            // Simplified test - just show message
            await Task.Delay(100);
            Status = "MIDI test not implemented in restructured version";
            MessageBox.Show("MIDI loopback test not yet implemented.", "Test Result", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void Dispose()
        {
            Logger.Info("Disposing MainViewModel");

            _bluetoothService.DeviceDiscovered -= OnDeviceDiscovered;
            _bluetoothService.DeviceDisconnected -= OnDeviceDisconnected;

            _bluetoothService.StopScanning();

            Logger.Info("MainViewModel disposed");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
