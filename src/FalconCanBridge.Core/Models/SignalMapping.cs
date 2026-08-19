using System;
using System.Text.Json.Serialization;

namespace FalconCanBridge.Core.Models;

/// <summary>
/// Direction of a mapping entry.
/// SimToCan: a simulator telemetry value is packed into an outgoing CAN frame (e.g. drive a gauge, LED, 7-segment display on the STM32 panel).
/// CanToSim: an incoming CAN frame (switch, encoder, button on the STM32 panel) is decoded and turned into a command sent back into the simulator.
/// </summary>
public enum MappingDirection
{
    SimToCan,
    CanToSim
}

/// <summary>How the value occupies bytes inside the 0-8 byte CAN payload.</summary>
public enum CanDataType
{
    Bit,
    UInt8,
    Int8,
    UInt16,
    Int16,
    UInt32,
    Int32,
    Float32
}

/// <summary>Which simulator this mapping applies to.</summary>
public enum SimulatorTarget
{
    Any,
    Falcon4Bms,
    Dcs
}

/// <summary>
/// A single field-level mapping between a named simulator signal and a location
/// (byte/bit offset + data type) inside a specific CAN frame identifier.
/// Several mappings can share the same <see cref="CanId"/> to pack multiple signals
/// into one 8-byte frame (e.g. bits 0-2 = gear lights, byte 1 = flap position).
/// </summary>
public sealed class SignalMapping
{
    /// <summary>Stable identifier for this row, used for UI editing and persistence diffing.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = string.Empty;

    public MappingDirection Direction { get; set; } = MappingDirection.SimToCan;

    public SimulatorTarget Target { get; set; } = SimulatorTarget.Any;

    /// <summary>Simulator telemetry signal name (SimToCan) or, for CanToSim, informational only.</summary>
    public string SignalName { get; set; } = string.Empty;

    /// <summary>
    /// For CanToSim mappings: the command to invoke in the simulator connector
    /// (a BMS key-binding name, or a DCS-BIOS control identifier such as "UFC_1").
    /// </summary>
    public string CommandName { get; set; } = string.Empty;

    public uint CanId { get; set; }

    public bool ExtendedId { get; set; }

    /// <summary>Byte offset (0-7) of the field's first byte within the 8-byte payload.</summary>
    public int ByteOffset { get; set; }

    /// <summary>For <see cref="CanDataType.Bit"/> only: bit position (0-7) within the byte at ByteOffset.</summary>
    public int BitOffset { get; set; }

    public CanDataType DataType { get; set; } = CanDataType.UInt8;

    public bool LittleEndian { get; set; } = true;

    /// <summary>Engineering value = (rawValue * Scale) + Offset. Applied in reverse when encoding CanToSim raw values back out.</summary>
    public double Scale { get; set; } = 1.0;

    public double Offset { get; set; }

    public double MinValue { get; set; } = double.NegativeInfinity;

    public double MaxValue { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// SimToCan only: minimum milliseconds between transmissions of the frame containing this
    /// signal, to avoid saturating the CAN bus with high-rate telemetry (e.g. AoA at 60+ Hz).
    /// The smallest SendRateMs among all mappings sharing a CanId governs that frame's throttle.
    /// </summary>
    public int SendRateMs { get; set; } = 50;

    /// <summary>
    /// SimToCan only: only re-send when the encoded raw value changes by at least this much.
    /// Set to 0 to always send on every telemetry tick (subject to SendRateMs).
    /// </summary>
    public double ChangeThreshold { get; set; }

    /// <summary>Free-text documentation shown in the mapping editor; not used by the engine.</summary>
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public int ByteWidth => DataType switch
    {
        CanDataType.Bit => 1,
        CanDataType.UInt8 or CanDataType.Int8 => 1,
        CanDataType.UInt16 or CanDataType.Int16 => 2,
        CanDataType.UInt32 or CanDataType.Int32 or CanDataType.Float32 => 4,
        _ => 1
    };

    public override string ToString() => $"{Name} [{Direction}] CAN 0x{CanId:X} off={ByteOffset} type={DataType}";
}
