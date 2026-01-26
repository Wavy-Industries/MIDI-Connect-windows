using System;

namespace MinimalWindowsApp.Midi
{
    public interface IVirtualMidiPort : IDisposable
    {
        string PortName { get; }
        bool IsOpen { get; }

        event EventHandler<byte[]>? DataReceived;

        bool Create(uint maxSysexLength, uint flags);
        bool SendData(byte[] data);
        void StartReceive();
        void Close();
    }
}
