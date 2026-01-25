using System;
using System.Threading.Tasks;
using TobiasErichsen.teVirtualMIDI;
using System.Runtime.InteropServices;

namespace MinimalWindowsApp.Managers
{
    public class VirtualMidiPort : IDisposable
    {
        public const uint PARSE_RX = TeVirtualMIDI.TE_VM_FLAGS_PARSE_RX;
        public const uint PARSE_TX = TeVirtualMIDI.TE_VM_FLAGS_PARSE_TX;
        public const uint INSTANTIATE_RX = TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_RX_ONLY;
        public const uint INSTANTIATE_TX = TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_TX_ONLY;
        public const uint INSTANTIATE_BOTH = TeVirtualMIDI.TE_VM_FLAGS_INSTANTIATE_BOTH;
        
        public const uint MAX_SYSEX_SIZE = 1024;
        public const uint MAX_SYSEX_SIZE_LARGE = 65535;
        
        private TeVirtualMIDI _midiPort;
        private readonly string _portName;
        private readonly ulong _bluetoothAddress;
        private Task _receiveTask;
        private bool _isReceiving;
        private bool _disposed;
        
        public event EventHandler<byte[]> DataReceived;
        
        public string PortName => _portName;
        public ulong BluetoothAddress => _bluetoothAddress;
        public bool IsOpen => _midiPort != null;
        
        static VirtualMidiPort()
        {
            try
            {
                Logger.Info($"[TEVIRTUALMIDI STATIC] TeVirtualMIDI Version: {TeVirtualMIDI.versionMajor}.{TeVirtualMIDI.versionMinor}.{TeVirtualMIDI.versionRelease}.{TeVirtualMIDI.versionBuild}");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TeVirtualMIDI Driver Version: {TeVirtualMIDI.driverVersionMajor}.{TeVirtualMIDI.driverVersionMinor}.{TeVirtualMIDI.driverVersionRelease}.{TeVirtualMIDI.driverVersionBuild}");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TeVirtualMIDI Version String: '{TeVirtualMIDI.versionString}'");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TeVirtualMIDI Driver Version String: '{TeVirtualMIDI.driverVersionString}'");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TE_VM_FLAGS_PARSE_RX: {PARSE_RX}");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TE_VM_FLAGS_PARSE_TX: {PARSE_TX}");
                Logger.Info($"[TEVIRTUALMIDI STATIC] TE_VM_FLAGS_INSTANTIATE_BOTH: {INSTANTIATE_BOTH}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[TEVIRTUALMINI STATIC] Failed to get TeVirtualMIDI driver information: {ex.Message}");
                Logger.LogDebug($"[TEVIRTUALMIDI STATIC] Exception details: {ex}");
            }
        }
        
        public VirtualMidiPort(string deviceName, ulong bluetoothAddress)
        {
            _portName = $"BLE MIDI: {deviceName}";
            _bluetoothAddress = bluetoothAddress;
        }
        
        public bool Create(uint maxSysexLength = MAX_SYSEX_SIZE, uint flags = INSTANTIATE_BOTH)
        {
            try
            {
                if (_midiPort != null)
                {
                    Logger.Warning($"Virtual MIDI port already exists for {_portName}, closing existing port first");
                    try
                    {
                        _midiPort.shutdown();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Error shutting down existing port: {ex.Message}");
                    }
                    _midiPort = null;
                }
                
                Logger.Info($"Creating virtual MIDI port '{_portName}'");
                _midiPort = new TeVirtualMIDI(_portName, maxSysexLength, flags);
                Logger.Info($"Virtual MIDI port '{_portName}' created successfully");
                return true;
            }
            catch (TeVirtualMIDIException ex)
            {
                Logger.Error($"Failed to create virtual MIDI port '{_portName}': {ex.Message} (Error code: {ex.reasonCode})");
                Logger.LogDebug($"TeVirtualMIDI exception details: {ex}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error creating virtual MIDI port '{_portName}': {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
                return false;
            }
        }
        
        public void StartReceive()
        {
            if (_midiPort == null)
            {
                Logger.Error($"Cannot start receiving: MIDI port not created");
                return;
            }
            
            if (_isReceiving)
            {
                Logger.Warning($"Receive task already running for port '{_portName}'");
                return;
            }
            
            try
            {
                Logger.Info($"Starting receive task for port '{_portName}'");
                _isReceiving = true;
                _receiveTask = Task.Run(async () =>
                {
                    while (_isReceiving && !_disposed)
                    {
                        try
                        {
                            var data = _midiPort.getCommand();
                            if (data != null && data.Length > 0)
                            {
                                Logger.LogDebug($"MIDI data received on port '{_portName}' ({data.Length} bytes): {BitConverter.ToString(data)}");
                                DataReceived?.Invoke(this, data);
                            }
                        }
                        catch (TeVirtualMIDIException ex)
                        {
                            if (ex.reasonCode == 6)
                            {
                                Logger.Info($"Port '{_portName}' closed, stopping receive task");
                                break;
                            }
                            Logger.Error($"Error receiving data on port '{_portName}': {ex.Message} (Error code: {ex.reasonCode})");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Unexpected error in receive loop for port '{_portName}': {ex.Message}");
                            Logger.LogDebug($"Exception details: {ex}");
                        }
                        
                        await Task.Delay(10);
                    }
                    Logger.Info($"Receive task stopped for port '{_portName}'");
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start receive task for port '{_portName}': {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
            }
        }
        
        public void SetDataReceivedCallback(Action<byte[]> callback)
        {
            DataReceived += (sender, data) => callback(data);
        }
        
        public bool SendData(byte[] data)
        {
            if (_midiPort == null)
            {
                Logger.Error($"Cannot send data: MIDI port not created for '{_portName}'");
                return false;
            }
            
            try
            {
                Logger.Info($"VirtualMidiPort.SendData() called - Port: '{_portName}', Data: {BitConverter.ToString(data)} ({data.Length} bytes)");
                _midiPort.sendCommand(data);
                Logger.Info($"MIDI data sent successfully on virtual port '{_portName}'");
                return true;
            }
            catch (TeVirtualMIDIException ex)
            {
                Logger.Error($"Failed to send data on port '{_portName}': {ex.Message} (Error code: {ex.reasonCode})");
                Logger.LogDebug($"TeVirtualMIDI exception details: {ex}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error sending data on port '{_portName}': {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
                return false;
            }
        }
        
        public void Close()
        {
            try
            {
                Logger.Info($"VirtualMidiPort.Close() called for port '{_portName}'");
                
                if (_isReceiving)
                {
                    Logger.Info($"Stopping receive task for port '{_portName}'");
                    _isReceiving = false;
                    _receiveTask?.Wait(TimeSpan.FromSeconds(2));
                }
                
                if (_midiPort != null)
                {
                    Logger.Info($"Closing virtual MIDI port '{_portName}'");
                    _midiPort.shutdown();
                    _midiPort = null;
                    Logger.Info($"Virtual MIDI port '{_portName}' closed successfully");
                }
                else
                {
                    Logger.LogDebug($"VirtualMidiPort.Close() - port already closed for '{_portName}'");
                }
            }
            catch (TeVirtualMIDIException ex)
            {
                Logger.Error($"Error closing port '{_portName}': {ex.Message} (Error code: {ex.reasonCode})");
                Logger.LogDebug($"TeVirtualMIDI exception details: {ex}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error closing port '{_portName}': {ex.Message}");
                Logger.LogDebug($"Exception details: {ex}");
            }
            finally
            {
                _midiPort = null;
            }
        }
        
        private void StopReceiving()
        {
            if (_isReceiving)
            {
                Logger.Info($"Stopping receive loop for port '{_portName}'");
                _isReceiving = false;
            }
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                Logger.Info($"VirtualMidiPort.Dispose() called for '{_portName}'");
                _disposed = true;
                StopReceiving();
                Close();
                Logger.Info($"VirtualMidiPort '{_portName}' disposed");
            }
            else
            {
                Logger.Warning($"VirtualMidiPort.Dispose() called on already disposed port '{_portName}'");
            }
        }
    }
}
