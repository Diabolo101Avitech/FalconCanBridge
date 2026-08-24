namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>Primitive type of a value stored inside the BMS shared-memory block.</summary>
public enum Falcon4FieldType
{
    Float32,
    Int32,
    UInt32,
    Int16,
    UInt16,
    Byte,
    /// <summary>Signed 8-bit value (e.g. BMS's iffTransponderActiveCode1, where negative = OFF/n.a.).</summary>
    SByte,
    /// <summary>A single bit inside the byte at ByteOffset, bit index given by BitIndex.</summary>
    Bit
}

/// <summary>
/// Describes how to pull one named telemetry value out of a raw "FalconSharedMemoryArea"/
/// "FalconSharedMemoryArea2" memory-mapped buffer.
///
/// The app's built-in default table (see <see cref="Falcon4FieldMap.BuildDefault"/>/
/// <see cref="Falcon4FieldMap.BuildSecondaryDefault"/>) is generated from vendored copies of BMS's
/// own struct layout (lightningstools/F4SharedMem), with offsets computed by the .NET marshaler
/// rather than hand-typed - see <c>Falcon4NativeFieldMapBuilder</c>. An optional
/// config/falcon4-fields.sample.json next to the exe can still add/override individual entries by
/// <see cref="Name"/> without recompiling. Either way, verify signals for your BMS version using
/// the Live Telemetry tab before trusting any value on real hardware.
/// </summary>
public sealed class Falcon4FieldDefinition
{
    public string Name { get; set; } = string.Empty;

    public int ByteOffset { get; set; }

    public Falcon4FieldType Type { get; set; } = Falcon4FieldType.Float32;

    public int BitIndex { get; set; }

    public double Scale { get; set; } = 1.0;

    public double Offset { get; set; }

    /// <summary>Free-text note, e.g. units or verification status ("verified" / "best effort - confirm offset").</summary>
    public string Notes { get; set; } = string.Empty;
}
