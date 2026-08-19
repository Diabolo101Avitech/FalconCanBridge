using System;

namespace FalconCanBridge.Core.Models;

/// <summary>
/// Direction a CAN frame travelled, relative to this application.
/// </summary>
public enum CanFrameDirection
{
    Tx,
    Rx
}

/// <summary>
/// A single classic CAN 2.0 frame (up to 8 data bytes). CAN-FD is not modelled;
/// the STM32 side is assumed to run classic CAN at a fixed bitrate (commonly 500 kbit/s
/// or 1 Mbit/s for cockpit panel buses).
/// </summary>
public sealed class CanFrame
{
    /// <summary>11-bit standard or 29-bit extended identifier.</summary>
    public uint Id { get; init; }

    public bool IsExtended { get; init; }

    public bool IsRemoteRequest { get; init; }

    /// <summary>0-8 bytes of payload.</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public int Dlc => Data.Length;

    public CanFrameDirection Direction { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public CanFrame() { }

    public CanFrame(uint id, byte[] data, bool extended = false, CanFrameDirection direction = CanFrameDirection.Tx)
    {
        if (data.Length > 8)
        {
            throw new ArgumentException("Classic CAN frames support at most 8 data bytes.", nameof(data));
        }

        Id = id;
        Data = data;
        IsExtended = extended;
        Direction = direction;
    }

    public override string ToString()
    {
        string idStr = IsExtended ? $"{Id:X8}" : $"{Id:X3}";
        string dataStr = BitConverter.ToString(Data).Replace('-', ' ');
        return $"[{Direction}] ID={idStr}{(IsExtended ? "x" : "")} DLC={Dlc} DATA={dataStr}";
    }
}
