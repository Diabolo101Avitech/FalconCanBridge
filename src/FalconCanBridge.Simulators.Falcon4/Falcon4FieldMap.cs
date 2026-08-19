using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// Holds the table of <see cref="Falcon4FieldDefinition"/> entries and decodes them out of a
/// raw shared-memory byte buffer into named engineering-unit values.
///
/// The default table below covers the block of values that has, in practice, stayed most
/// stable at the *start* of the classic BMS "FlightData" structure across versions (basic
/// body-axis position/attitude/rates and the primary airspeed/altitude instruments). It is a
/// best-effort starting point copied from the layout long used by community DIY-cockpit
/// projects, NOT a byte-for-byte guarantee for your exact BMS build - always cross-check
/// against FalconSharedMemoryArea.h from your BMS installation or the F4SharedMem project,
/// and use the app's raw hex / signal-scanner view to confirm before wiring real panels.
/// </summary>
public sealed class Falcon4FieldMap
{
    public List<Falcon4FieldDefinition> Fields { get; private set; } = BuildDefault();

    public static Falcon4FieldMap LoadFromFile(string path)
    {
        var map = new Falcon4FieldMap();
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var loaded = JsonSerializer.Deserialize<List<Falcon4FieldDefinition>>(json, options);
        if (loaded is { Count: > 0 })
        {
            map.Fields = loaded;
        }
        return map;
    }

    public static void SaveToFile(string path, List<Falcon4FieldDefinition> fields)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(fields, options));
    }

    /// <summary>Decodes every configured field out of <paramref name="raw"/> into a name-&gt;value dictionary.</summary>
    public Dictionary<string, double> Decode(ReadOnlySpan<byte> raw)
    {
        var result = new Dictionary<string, double>(Fields.Count);

        foreach (var f in Fields)
        {
            if (f.ByteOffset < 0 || f.ByteOffset >= raw.Length) continue;

            double rawValue = f.Type switch
            {
                Falcon4FieldType.Float32 when f.ByteOffset + 4 <= raw.Length
                    => BitConverter.ToSingle(raw.Slice(f.ByteOffset, 4)),
                Falcon4FieldType.Int32 when f.ByteOffset + 4 <= raw.Length
                    => BitConverter.ToInt32(raw.Slice(f.ByteOffset, 4)),
                Falcon4FieldType.UInt32 when f.ByteOffset + 4 <= raw.Length
                    => BitConverter.ToUInt32(raw.Slice(f.ByteOffset, 4)),
                Falcon4FieldType.Int16 when f.ByteOffset + 2 <= raw.Length
                    => BitConverter.ToInt16(raw.Slice(f.ByteOffset, 2)),
                Falcon4FieldType.UInt16 when f.ByteOffset + 2 <= raw.Length
                    => BitConverter.ToUInt16(raw.Slice(f.ByteOffset, 2)),
                Falcon4FieldType.Byte
                    => raw[f.ByteOffset],
                Falcon4FieldType.Bit
                    => (raw[f.ByteOffset] & (1 << Math.Clamp(f.BitIndex, 0, 7))) != 0 ? 1.0 : 0.0,
                _ => 0.0
            };

            result[f.Name] = rawValue * f.Scale + f.Offset;
        }

        return result;
    }

    /// <summary>
    /// Best-effort default field table. Offsets assume the classic little-endian, 4-byte
    /// packed "FlightData" block starting at offset 0 of the "FalconSharedMemoryArea"
    /// memory-mapped file. Angles are exposed in degrees, speeds in knots, altitude in feet.
    /// </summary>
    public static List<Falcon4FieldDefinition> BuildDefault() => new()
    {
        new() { Name = "PositionX",     ByteOffset = 0,   Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "PositionY",     ByteOffset = 4,   Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "PositionZ",     ByteOffset = 8,   Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "VelocityX",     ByteOffset = 12,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "VelocityY",     ByteOffset = 16,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "VelocityZ",     ByteOffset = 20,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "AngleOfAttackDeg", ByteOffset = 24, Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "SideslipDeg",   ByteOffset = 28,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "FlightPathDeg", ByteOffset = 32,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "PitchDeg",      ByteOffset = 36,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "RollDeg",       ByteOffset = 40,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "YawDeg",        ByteOffset = 44,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "Mach",          ByteOffset = 48,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "AirspeedKias",  ByteOffset = 52,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "TrueAirspeedFtSec", ByteOffset = 56, Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "AltitudeMslFt", ByteOffset = 60,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
        new() { Name = "AltitudeAglFt", ByteOffset = 64,  Type = Falcon4FieldType.Float32, Notes = "best effort - confirm offset" },
    };
}
