using System;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.Interfaces;

public sealed class CanFrameReceivedEventArgs : EventArgs
{
    public CanFrame Frame { get; }
    public CanFrameReceivedEventArgs(CanFrame frame) => Frame = frame;
}

public sealed class CanFrameTransmittedEventArgs : EventArgs
{
    public CanFrame Frame { get; }
    public CanFrameTransmittedEventArgs(CanFrame frame) => Frame = frame;
}

/// <summary>
/// Abstraction over a physical/virtual CAN interface. The primary implementation targets
/// an STM32-based USB-CDC "CAN adapter" board speaking the SLCAN (LAWICEL) ASCII protocol,
/// which is the de-facto open standard implemented by most hobbyist STM32 CAN-USB firmwares
/// and understood by Linux SocketCAN's `slcand`. A PCAN-Basic adapter is also provided for
/// users who bridge to the STM32 nodes through a PEAK PCAN-USB dongle instead.
/// </summary>
public interface ICanBusAdapter : IDisposable
{
    string Name { get; }

    bool IsOpen { get; }

    event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;

    event EventHandler<CanFrameTransmittedEventArgs>? FrameTransmitted;

    event EventHandler<string>? LogMessage;

    event EventHandler? ConnectionStateChanged;

    /// <summary>
    /// Opens the adapter. <paramref name="connectionString"/> is adapter-specific
    /// (e.g. "COM5;500000" for the serial/SLCAN adapter, or a PCAN channel name).
    /// </summary>
    Task OpenAsync(string connectionString, CancellationToken cancellationToken = default);

    Task CloseAsync();

    Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default);
}
