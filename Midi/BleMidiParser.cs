using System;
using System.Collections.Generic;

namespace MIDIConnect.Midi
{
    /// <summary>
    /// BLE MIDI packet parser conforming to the "MIDI over Bluetooth Low Energy" specification
    /// (MIDI Association / Apple, document: "Bluetooth LE MIDI Specification, Revision 1.0").
    ///
    /// ── PACKET STRUCTURE ──────────────────────────────────────────────────────────────────
    ///
    ///   [Header] [Timestamp] [Status] [Data...] [Timestamp] [Status] [Data...] ...
    ///
    ///   Header byte  (exactly 1 per packet, always the first byte):
    ///     Bit  7   : 1  (always set — identifies this byte as the packet header)
    ///     Bit  6   : 0  (always clear per spec)
    ///     Bits 5-0 : High 6 bits of the 13-bit millisecond timestamp
    ///
    ///   Timestamp byte  (1 per MIDI event, immediately before each message):
    ///     Bit  7   : 1  (always set — distinguishes timestamp bytes from MIDI data bytes)
    ///     Bits 6-0 : Low 7 bits of the 13-bit millisecond timestamp
    ///
    ///   Full 13-bit timestamp = (header[5:0] << 7) | timestamp[6:0]
    ///   Resolution: 1 ms per tick; wraps every ~8.192 seconds.
    ///
    /// ── MIDI EVENT AFTER EACH TIMESTAMP ───────────────────────────────────────────────────
    ///
    ///   If the byte following the timestamp has bit 7 SET   → new MIDI status byte.
    ///   If the byte following the timestamp has bit 7 CLEAR → running status applies
    ///     (current byte is the first data byte; status is inherited from the previous message).
    ///
    ///   MIDI data bytes always have bit 7 clear (0x00–0x7F).
    ///
    /// ── RUNNING STATUS ────────────────────────────────────────────────────────────────────
    ///
    ///   Consecutive messages of the same Channel Voice type may omit the status byte,
    ///   following standard MIDI running status rules. The timestamp byte is still always
    ///   present before each event even when running status is active.
    ///   Running status is cancelled by SysEx (0xF0) and all System Common messages.
    ///   System Realtime messages (0xF8–0xFF) do NOT cancel running status.
    ///
    /// ── SYSEX ─────────────────────────────────────────────────────────────────────────────
    ///
    ///   SysEx (0xF0 … 0xF7) may span multiple BLE packets:
    ///     - First packet  : [Header][Timestamp][0xF0][data…]
    ///     - Middle packets: [Header][data…]              (no leading timestamp)
    ///     - Last packet   : [Header][data…][Timestamp][0xF7]
    ///   This parser handles single-packet SysEx in full. Multi-packet SysEx requires
    ///   the caller to reassemble raw bytes across packets before calling ParseBlePacket,
    ///   or to detect continuation packets (first payload byte has bit 7 clear).
    ///
    /// ── MIDI MESSAGE DATA LENGTHS (bytes following status) ────────────────────────────────
    ///
    ///   Note Off              0x80–0x8F  2 bytes
    ///   Note On               0x90–0x9F  2 bytes
    ///   Poly Aftertouch       0xA0–0xAF  2 bytes
    ///   Control Change        0xB0–0xBF  2 bytes
    ///   Program Change        0xC0–0xCF  1 byte
    ///   Channel Pressure      0xD0–0xDF  1 byte
    ///   Pitch Bend            0xE0–0xEF  2 bytes
    ///   SysEx                 0xF0       variable, terminated by 0xF7
    ///   MTC Quarter Frame     0xF1       1 byte
    ///   Song Position Pointer 0xF2       2 bytes
    ///   Song Select           0xF3       1 byte
    ///   Tune Request          0xF6       0 bytes
    ///   End of Exclusive      0xF7       0 bytes  (standalone — closes an open SysEx)
    ///   System Realtime       0xF8–0xFF  0 bytes  (single-byte; transparent to running status)
    ///
    /// </summary>
    public static class BleMidiParser
    {
        /// <summary>
        /// Parse a BLE MIDI packet into individual raw MIDI messages.
        ///
        /// Each returned byte[] is a complete MIDI message: [status, data0, data1, …].
        /// SysEx messages include the opening 0xF0 and closing 0xF7 bytes.
        /// System Realtime messages are returned as single-byte arrays.
        /// </summary>
        /// <param name="bleData">Raw bytes received from the BLE MIDI GATT characteristic.</param>
        /// <returns>Array of complete MIDI messages extracted from the packet.</returns>
        public static byte[][] ParseBlePacket(byte[] bleData)
        {
            if (bleData == null || bleData.Length < 2)
                return Array.Empty<byte[]>();

            // ── Byte 0: Packet Header ─────────────────────────────────────────────────────
            // Bit 7 must be 1. Bit 6 must be 0. Bits 5-0 are the high timestamp bits.
            // One header per packet — NOT one per message (the old implementation was wrong here).
            byte header = bleData[0];
            if ((header & 0x80) == 0)
            {
                Infrastructure.Logger.Warning($"[BLE MIDI] Invalid packet: header byte 0x{header:X2} does not have bit 7 set");
                return Array.Empty<byte[]>();
            }

            // Extract the high 6 bits of the timestamp, pre-shifted into position.
            int timestampHigh = (header & 0x3F) << 7;

            var messages = new List<byte[]>();
            int i = 1;            // current read position; starts after the header
            byte runningStatus = 0;

            while (i < bleData.Length)
            {
                // ── Timestamp byte ────────────────────────────────────────────────────────
                // Every MIDI event is preceded by exactly one timestamp byte (bit 7 set).
                // Exception: SysEx continuation packets start with data bytes directly after
                // the header — the caller is responsible for detecting and handling those.
                byte b = bleData[i];
                if ((b & 0x80) == 0)
                {
                    // A data byte appearing here (bit 7 clear) indicates either a malformed
                    // packet or a multi-packet SysEx continuation that the caller did not
                    // pre-filter. Stop parsing — do not silently corrupt remaining events.
                    Infrastructure.Logger.Warning(
                        $"[BLE MIDI] Expected timestamp byte at offset {i}, got data byte 0x{b:X2}. " +
                        "This may be a SysEx continuation packet — reassemble across packets before parsing.");
                    break;
                }

                // Low 7 bits of the timestamp. Combined with timestampHigh this gives the
                // full 13-bit millisecond timestamp for the event, though we don't currently
                // use it for scheduling (passed through for completeness).
                int timestampLow = b & 0x7F;
                int timestamp = timestampHigh | timestampLow; // 0–8191 ms
                i++;

                if (i >= bleData.Length)
                    break;

                // ── Status byte or running-status data ───────────────────────────────────
                b = bleData[i];
                byte status;

                if ((b & 0x80) != 0)
                {
                    // Byte has bit 7 set → it is a MIDI status byte.
                    status = b;
                    i++;

                    // System Realtime messages (0xF8–0xFF) are single-byte, may appear
                    // anywhere in the stream, and do NOT update or cancel running status.
                    if (status >= 0xF8)
                    {
                        messages.Add(new byte[] { status });
                        continue;
                    }

                    // SysEx (0xF0) and System Common messages (0xF1–0xF7) cancel running status.
                    // Channel Voice messages (0x80–0xEF) update running status.
                    if (status < 0xF0)
                        runningStatus = status;     // Channel Voice — update running status
                    else
                        runningStatus = 0;          // System Common — cancel running status
                }
                else
                {
                    // Byte has bit 7 clear → data byte; apply running status.
                    // Do NOT advance i — this byte is the first data byte of the message.
                    if (runningStatus == 0)
                    {
                        Infrastructure.Logger.Warning(
                            $"[BLE MIDI] Data byte 0x{b:X2} at offset {i} received with no active running status — skipping");
                        i++;
                        continue;
                    }
                    status = runningStatus;
                }

                // ── SysEx (variable length) ───────────────────────────────────────────────
                if (status == 0xF0)
                {
                    var sysex = new List<byte> { 0xF0 };

                    while (i < bleData.Length)
                    {
                        byte sb = bleData[i];

                        if (sb == 0xF7)
                        {
                            // End of Exclusive — SysEx is complete.
                            sysex.Add(0xF7);
                            i++;
                            break;
                        }

                        if ((sb & 0x80) != 0)
                        {
                            // A byte with bit 7 set inside a SysEx body is either:
                            //   (a) A timestamp byte that precedes the closing 0xF7, or
                            //   (b) An embedded System Realtime message (0xF8–0xFF).
                            // Per spec, no other status byte is valid inside SysEx.
                            if (sb >= 0xF8)
                            {
                                // Embedded System Realtime — emit it immediately, then continue SysEx.
                                messages.Add(new byte[] { sb });
                            }
                            // Otherwise it is a timestamp byte — skip it and keep reading SysEx data.
                            i++;
                            continue;
                        }

                        sysex.Add(sb);
                        i++;
                    }

                    messages.Add(sysex.ToArray());
                    runningStatus = 0; // SysEx always cancels running status
                    continue;
                }

                // ── Fixed-length Channel Voice / System Common message ─────────────────────
                int dataLength = GetMidiDataLength(status);
                var msg = new byte[1 + dataLength];
                msg[0] = status;
                bool complete = true;

                for (int d = 0; d < dataLength; d++)
                {
                    if (i >= bleData.Length)
                    {
                        Infrastructure.Logger.Warning(
                            $"[BLE MIDI] Packet ended prematurely: expected {dataLength} data bytes for " +
                            $"status 0x{status:X2}, only got {d}");
                        complete = false;
                        break;
                    }

                    byte db = bleData[i];
                    if ((db & 0x80) != 0)
                    {
                        // Hit a timestamp byte (next event) before all data bytes were consumed.
                        // This should not happen in a well-formed packet.
                        Infrastructure.Logger.Warning(
                            $"[BLE MIDI] Unexpected high byte 0x{db:X2} at offset {i} while reading " +
                            $"data byte {d + 1}/{dataLength} for status 0x{status:X2}");
                        complete = false;
                        break;
                    }

                    msg[1 + d] = db;
                    i++;
                }

                if (complete)
                    messages.Add(msg);
            }

            return messages.ToArray();
        }

        /// <summary>
        /// Returns the number of data bytes that follow a given MIDI status byte.
        /// Returns -1 for SysEx (0xF0), which is variable-length; the caller must
        /// scan for the 0xF7 End of Exclusive byte instead.
        /// </summary>
        private static int GetMidiDataLength(byte status)
        {
            // Channel Voice messages: upper nibble selects type, lower nibble is channel (0–15).
            return (status & 0xF0) switch
            {
                0x80 => 2,  // Note Off
                0x90 => 2,  // Note On  (velocity 0 = implicit Note Off under running status)
                0xA0 => 2,  // Polyphonic Key Pressure (Aftertouch)
                0xB0 => 2,  // Control Change (also Channel Mode when controller >= 120)
                0xC0 => 1,  // Program Change
                0xD0 => 1,  // Channel Pressure (mono Aftertouch)
                0xE0 => 2,  // Pitch Bend Change (14-bit value, LSB first)

                // System messages: identified by their full byte value.
                _ => status switch
                {
                    0xF0 => -1, // System Exclusive (variable — handled separately)
                    0xF1 =>  1, // MIDI Time Code Quarter Frame
                    0xF2 =>  2, // Song Position Pointer
                    0xF3 =>  1, // Song Select
                    0xF4 =>  0, // Undefined (reserved)
                    0xF5 =>  0, // Undefined (reserved)
                    0xF6 =>  0, // Tune Request
                    0xF7 =>  0, // End of SysEx (standalone — no data bytes)
                    // 0xF8–0xFF: System Realtime — intercepted before reaching here
                    _    =>  0
                }
            };
        }

        /// <summary>
        /// Wrap a raw MIDI message with BLE MIDI framing for transmission.
        ///
        /// Output layout:  [Header][TimestampLow][MIDI status][MIDI data…]
        ///
        ///   Header byte    : bit7=1, bit6=0, bits[5:0] = timestamp[12:7]  (high 6 bits)
        ///   Timestamp byte : bit7=1,          bits[6:0] = timestamp[6:0]  (low 7 bits)
        ///
        /// The timestamp is a 13-bit millisecond counter (0–8191 ms, wraps around).
        /// Pass 0 when precise timing is not required.
        ///
        /// Note: This produces a single-event packet. The spec allows batching multiple
        /// events into one packet (shared header, one timestamp per event) but that
        /// optimisation is not needed for outbound messages from this application.
        /// </summary>
        /// <param name="midiData">Complete raw MIDI message (status byte + data bytes).</param>
        /// <param name="timestamp">13-bit millisecond timestamp (values above 8191 are clamped).</param>
        /// <returns>BLE MIDI packet ready to write to the GATT characteristic.</returns>
        public static byte[] WrapMidiForBle(byte[] midiData, ushort timestamp = 0)
        {
            if (midiData == null || midiData.Length == 0)
                return Array.Empty<byte>();

            // Clamp to 13 bits (maximum value: 8191 ms).
            timestamp &= 0x1FFF;

            // Header: bit7=1, bit6=0, bits[5:0] = high 6 bits of timestamp.
            byte header = (byte)(0x80 | ((timestamp >> 7) & 0x3F));

            // Timestamp low: bit7=1, bits[6:0] = low 7 bits of timestamp.
            byte timestampLow = (byte)(0x80 | (timestamp & 0x7F));

            var bleData = new byte[2 + midiData.Length];
            bleData[0] = header;
            bleData[1] = timestampLow;
            Buffer.BlockCopy(midiData, 0, bleData, 2, midiData.Length);

            return bleData;
        }
    }
}
