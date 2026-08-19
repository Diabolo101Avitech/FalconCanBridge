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
    /// <summary>A single bit inside the byte at ByteOffset, bit index given by BitIndex.</summary>
    Bit
}

/// <summary>
/// Describes how to pull one named telemetry value out of the raw
/// "FalconSharedMemoryArea" memory-mapped buffer.
///
/// IMPORTANT - offsets are configuration, not hardcoded C structs, precisely because the
/// exact byte layout of BMS's shared memory changes slightly between versions and is only
/// authoritatively defined by the FalconSharedMemoryArea.h header shipped with each BMS
/// install (and by the community-maintained F4SharedMem reference project). Ship this app
/// with the bundled defaults in config/falcon4-fields.sample.json, but treat them as a
/// starting point: verify/correct offsets for your BMS version using the raw hex/"signal
/// scanner" diagnostic view in the app before trusting any value for real hardware.
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
