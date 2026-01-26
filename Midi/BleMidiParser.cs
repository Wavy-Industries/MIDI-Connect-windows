using System;
using System.Collections.Generic;

namespace MinimalWindowsApp.Midi
{
    /// <summary>
    /// Minimal BLE MIDI packet parser.
    /// This parser assumes a simplified BLE MIDI structure where each message is exactly 5 bytes:
    /// [timestampHigh][timestampLow][status][data1][data2]
    /// Multiple messages can arrive in a single packet (length must be divisible by 5).
    /// This does NOT handle running status, SysEx, or variable-length messages per the full BLE MIDI spec.
    /// </summary>
    public static class BleMidiParser
    {
        private const int BLE_MIDI_MESSAGE_SIZE = 5; // 2 timestamp bytes + 3 MIDI message bytes

        /// <summary>
        /// Parse a BLE MIDI packet into individual 3-byte MIDI messages.
        /// </summary>
        /// <param name="bleData">Raw BLE MIDI packet data</param>
        /// <returns>Array of 3-byte MIDI messages</returns>
        public static byte[][] ParseBlePacket(byte[] bleData)
        {
            if (bleData == null || bleData.Length == 0)
            {
                return Array.Empty<byte[]>();
            }

            if (bleData.Length % BLE_MIDI_MESSAGE_SIZE != 0)
            {
                Infrastructure.Logger.Warning($"[BLE MIDI] Received data length {bleData.Length} is not divisible by {BLE_MIDI_MESSAGE_SIZE}. " +
                    $"This minimal parser expects exactly {BLE_MIDI_MESSAGE_SIZE}-byte messages. Data may be malformed or use unsupported BLE MIDI features.");
            }

            var messages = new List<byte[]>();
            int messageCount = bleData.Length / BLE_MIDI_MESSAGE_SIZE;

            for (int i = 0; i < messageCount; i++)
            {
                int offset = i * BLE_MIDI_MESSAGE_SIZE;
                // Skip timestamp bytes (offset+0 and offset+1), extract 3-byte MIDI message
                var midiMessage = new byte[3];
                midiMessage[0] = bleData[offset + 2]; // status byte
                midiMessage[1] = bleData[offset + 3]; // data1
                midiMessage[2] = bleData[offset + 4]; // data2
                messages.Add(midiMessage);
            }

            return messages.ToArray();
        }

        /// <summary>
        /// Wrap MIDI data with BLE MIDI header (timestamp bytes).
        /// </summary>
        /// <param name="midiData">Raw MIDI message data</param>
        /// <param name="timestamp">13-bit timestamp value</param>
        /// <returns>BLE MIDI packet with timestamp header</returns>
        public static byte[] WrapMidiForBle(byte[] midiData, ushort timestamp)
        {
            if (midiData == null || midiData.Length == 0)
            {
                return Array.Empty<byte>();
            }

            // BLE MIDI spec requires bit 7 set on both timestamp bytes
            var timestampHigh = (byte)(0x80 | ((timestamp >> 7) & 0x3F));
            var timestampLow = (byte)(0x80 | (timestamp & 0x7F));

            var bleData = new byte[2 + midiData.Length];
            bleData[0] = timestampHigh;
            bleData[1] = timestampLow;
            Buffer.BlockCopy(midiData, 0, bleData, 2, midiData.Length);

            return bleData;
        }
    }
}
