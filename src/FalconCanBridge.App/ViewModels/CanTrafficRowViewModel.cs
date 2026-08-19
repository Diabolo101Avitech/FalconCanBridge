using System;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.App.ViewModels;

public sealed class CanTrafficRowViewModel
{
    public DateTime Timestamp { get; }
    public CanFrameDirection Direction { get; }
    public string IdHex { get; }
    public int Dlc { get; }
    public string DataHex { get; }

    public CanTrafficRowViewModel(CanFrame frame)
    {
        Timestamp = frame.Timestamp;
        Direction = frame.Direction;
        IdHex = frame.IsExtended ? frame.Id.ToString("X8") : frame.Id.ToString("X3");
        Dlc = frame.Dlc;
        DataHex = BitConverter.ToString(frame.Data).Replace('-', ' ');
    }
}
