using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using MIDIConnect.Infrastructure;

namespace MIDIConnect.Midi
{
    public class GattMidiPort : IDisposable
    {
        private readonly GattCharacteristic _midiCharacteristic;
        private bool _disposed;

        public string DeviceId { get; }
        public string DeviceName { get; }
        public bool IsOpen { get; private set; }

        public event Action<byte[][]>? MidiMessagesReceived;

        public GattMidiPort(string deviceId, string deviceName, GattCharacteristic midiCharacteristic)
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

        private void OnMidiDataReceived(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue))
            {
                reader.ReadBytes(data);
            }

            if (data.Length == 0)
            {
                return;
            }

            // Parse BLE MIDI packet into individual MIDI messages
            var midiMessages = BleMidiParser.ParseBlePacket(data);

            if (midiMessages.Length > 0)
            {
                MidiMessagesReceived?.Invoke(midiMessages);
            }
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
