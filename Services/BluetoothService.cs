using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using MIDIConnect.Infrastructure;

namespace MIDIConnect.Services
{
    public class BluetoothService
    {
        private static BluetoothService? _instance;
        public static BluetoothService Instance => _instance ??= new BluetoothService();

        private readonly Dictionary<ulong, DeviceSession> _sessions = new();
        private readonly BluetoothLEAdvertisementWatcher _advertisementWatcher;

        public event Action<DeviceSession>? DeviceDiscovered;
        public event Action<ulong>? DeviceDisconnected;

        private BluetoothService()
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

        public DeviceSession? GetSession(ulong bluetoothAddress)
        {
            _sessions.TryGetValue(bluetoothAddress, out var session);
            return session;
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

                if (_sessions.ContainsKey(args.BluetoothAddress))
                {
                    Logger.LogDebug($"  -> Device already discovered, skipping");
                    return;
                }

                _ = TryCreateSessionAndNotify(args.BluetoothAddress);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to process MIDI device advertisement: {ex.Message}");
            }
        }

        private async Task TryCreateSessionAndNotify(ulong bluetoothAddress)
        {
            try
            {
                Logger.LogDebug($"Getting device info for address: 0x{bluetoothAddress:X}");
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (device != null)
                {
                    var actualName = device.Name ?? "Unknown";
                    Logger.Info($"Device discovered: {actualName} (ConnectionStatus: {device.ConnectionStatus})");

                    var session = new DeviceSession(device);
                    session.Disconnected += OnSessionDisconnected;
                    _sessions[bluetoothAddress] = session;
                    DeviceDiscovered?.Invoke(session);
                }
                else
                {
                    Logger.Warning($"Device from address 0x{bluetoothAddress:X} is null");
                    // Still create a session so we can try to connect later
                    var session = await DeviceSession.CreateFromAddress(bluetoothAddress);
                    if (session != null)
                    {
                        session.Disconnected += OnSessionDisconnected;
                        _sessions[bluetoothAddress] = session;
                        DeviceDiscovered?.Invoke(session);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get device from address 0x{bluetoothAddress:X}: {ex.Message}");
            }
        }

        private void OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Logger.Warning($"Advertisement watcher stopped: {args.Error}");
        }

        public void Shutdown()
        {
            Logger.Info("=== BluetoothService.Shutdown() starting ===");

            try
            {
                StopScanning();

                _advertisementWatcher.Received -= OnAdvertisementReceived;
                _advertisementWatcher.Stopped -= OnAdvertisementWatcherStopped;

                Logger.Info($"Disposing {_sessions.Count} active device sessions...");
                foreach (var kvp in _sessions.ToList())
                {
                    try
                    {
                        Logger.Info($"Disposing session for device 0x{kvp.Key:X}");
                        kvp.Value.Disconnected -= OnSessionDisconnected;
                        kvp.Value.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error disposing session 0x{kvp.Key:X}: {ex.Message}");
                    }
                }
                _sessions.Clear();

                Logger.Info("=== BluetoothService.Shutdown() completed ===");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during BluetoothService shutdown: {ex.Message}");
                Logger.LogDebug($"Shutdown exception details: {ex}");
            }
        }
    }
}
