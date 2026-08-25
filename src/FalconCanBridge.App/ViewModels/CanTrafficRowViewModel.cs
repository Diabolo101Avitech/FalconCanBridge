using System;
using FalconCanBridge.Core.CanOpen;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.App.ViewModels;

public sealed class CanTrafficRowViewModel
{
    public DateTime Timestamp { get; }
    public CanFrameDirection Direction { get; }
    public string IdHex { get; }
    public int Dlc { get; }
    public string DataHex { get; }

    /// <summary>
    /// Best-effort CANopen label for this frame's COB-ID (e.g. "Tpdo1 node 5", "Heartbeat node 3"),
    /// or blank if the ID doesn't match the predefined connection set - see <see cref="CanOpenCobId.TryDecode"/>.
    /// Shown regardless of whether CANopen is enabled in the Connections tab; it's purely an ID-pattern
    /// label and doesn't imply the bus is actually running CANopen.
    /// </summary>
    public string CanOpenLabel { get; }

    public CanTrafficRowViewModel(CanFrame frame)
    {
        Timestamp = frame.Timestamp;
        Direction = frame.Direction;
        IdHex = frame.IsExtended ? frame.Id.ToString("X8") : frame.Id.ToString("X3");
        Dlc = frame.Dlc;
        DataHex = BitConverter.ToString(frame.Data).Replace('-', ' ');

        CanOpenLabel = !frame.IsExtended && CanOpenCobId.TryDecode(frame.Id, out var function, out int nodeId)
            ? nodeId > 0 ? $"{function} node {nodeId}" : function.ToString()
            : string.Empty;
    }
}
