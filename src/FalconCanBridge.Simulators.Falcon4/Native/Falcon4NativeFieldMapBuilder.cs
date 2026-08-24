using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FalconCanBridge.Simulators.Falcon4;

namespace FalconCanBridge.Simulators.Falcon4.Native;

/// <summary>
/// Builds the comprehensive default <see cref="Falcon4FieldDefinition"/> tables for the primary
/// ("FalconSharedMemoryArea" / <see cref="BMS4FlightDataNative"/>) and secondary
/// ("FalconSharedMemoryArea2" / <see cref="FlightData2Native"/>) shared-memory blocks, entirely
/// from the vendored native struct/enum definitions in this folder - see the remarks on
/// <see cref="BMS4FlightDataNative"/> for why byte offsets are computed via
/// <see cref="Marshal.OffsetOf{T}(string)"/> here instead of being hand-typed.
///
/// Coverage: every scalar (non-array) field of both structs, every RWR contact slot (0..39) across
/// all seven numeric RWR arrays in the primary struct plus the RWR jamming-status array in the
/// secondary struct, and every individual named bit of lightBits/lightBits2/lightBits3/hsiBits
/// (primary) and altBits/powerBits/blinkBits/bettyBits/miscBits (secondary) - several hundred
/// signals in total, versus the ~17 hand-picked/guessed fields this project shipped with before
/// (several of which, on inspection, didn't correspond to any real field in BMS4FlightData at all -
/// e.g. there is no "AltitudeMslFt"/"AltitudeAglFt" field in the real struct).
///
/// Deliberately NOT covered here (documented, not silently dropped - see README "Known
/// limitations"): DED/PFL line text (see <see cref="DecodeDedPflText"/> instead - it's text, not a
/// numeric signal), the raw RwrInfo blob, RTT screen-region coordinates, MP pilot
/// callsigns/status, and the separate IntelliVibe/OSB label/RadioClient/String/Drawing
/// shared-memory areas (each is its own named shared-memory section with its own reader/decoder).
/// </summary>
internal static class Falcon4NativeFieldMapBuilder
{
    private const int DedPflLineLength = 26;
    private const int DedPflLineCount = 5;

    public static List<Falcon4FieldDefinition> BuildPrimaryFields()
    {
        var fields = new List<Falcon4FieldDefinition>();
        fields.AddRange(ScalarFieldsFrom<BMS4FlightDataNative>());

        int BaseOf(string name) => (int)Marshal.OffsetOf<BMS4FlightDataNative>(name);

        AddIndexedArray(fields, "RWRsymbol", BaseOf("RWRsymbol"), sizeof(int), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.Int32, "RWR contact symbol/type code");
        AddIndexedArray(fields, "bearing", BaseOf("bearing"), sizeof(float), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.Float32, "RWR contact bearing (radians)");
        AddIndexedArray(fields, "missileActivity", BaseOf("missileActivity"), sizeof(uint), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.UInt32, "RWR contact missile activity flag");
        AddIndexedArray(fields, "missileLaunch", BaseOf("missileLaunch"), sizeof(uint), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.UInt32, "RWR contact missile launch flag");
        AddIndexedArray(fields, "selected", BaseOf("selected"), sizeof(uint), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.UInt32, "RWR contact selected/highlighted flag");
        AddIndexedArray(fields, "lethality", BaseOf("lethality"), sizeof(float), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.Float32, "RWR contact lethality (0..1)");
        AddIndexedArray(fields, "newDetection", BaseOf("newDetection"), sizeof(uint), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.UInt32, "RWR contact newly-detected flag");

        AddBitFlags<LightBitsNative>(fields, "lightBits", BaseOf("lightBits"));
        AddBitFlags<LightBits2Native>(fields, "lightBits2", BaseOf("lightBits2"));
        AddBitFlags<LightBits3Native>(fields, "lightBits3", BaseOf("lightBits3"));
        AddBitFlags<HsiBitsNative>(fields, "hsiBits", BaseOf("hsiBits"));

        return fields;
    }

    public static List<Falcon4FieldDefinition> BuildSecondaryFields()
    {
        var fields = new List<Falcon4FieldDefinition>();
        fields.AddRange(ScalarFieldsFrom<FlightData2Native>());

        int BaseOf(string name) => (int)Marshal.OffsetOf<FlightData2Native>(name);

        AddIndexedArray(fields, "tacanInfo", BaseOf("tacanInfo"), sizeof(byte), FlightData2Native.TacanSourcesCount, Falcon4FieldType.Byte, "Tacan band/mode settings, index 0=UFC 1=AUX (see TacanBits/TacanSources upstream)");
        AddIndexedArray(fields, "ecmBits", BaseOf("ecmBits"), sizeof(uint), FlightData2Native.MaxEcmPrograms, Falcon4FieldType.UInt32, "ECM program state (see EcmBits enum upstream)");
        AddIndexedArray(fields, "RWRjammingStatus", BaseOf("RWRjammingStatus"), sizeof(byte), BMS4FlightDataNative.MaxRwrObjects, Falcon4FieldType.Byte, "RWR contact jamming status (see JammingStates enum upstream)");

        AddBitFlags<AltBitsNative>(fields, "altBits", BaseOf("altBits"));
        AddBitFlags<PowerBitsNative>(fields, "powerBits", BaseOf("powerBits"));
        AddBitFlags<BlinkBitsNative>(fields, "blinkBits", BaseOf("blinkBits"));
        AddBitFlags<BettyBitsNative>(fields, "bettyBits", BaseOf("bettyBits"));
        AddBitFlags<MiscBitsNative>(fields, "miscBits", BaseOf("miscBits"));

        return fields;
    }

    /// <summary>
    /// Decodes the primary struct's DED/PFL text lines (5 lines x 26 raw chars each) directly out
    /// of a raw shared-memory snapshot buffer, mirroring F4SharedMem.FlightData's own decode logic:
    /// DEDLines/PFLLines carry plain text (0 = blank), while Invert/PFLInvert mark which character
    /// cells are shown in reverse video via a 0x02 marker byte rather than carrying visible text.
    /// </summary>
    public static Dictionary<string, string> DecodeDedPflText(ReadOnlySpan<byte> primaryBuffer)
    {
        var result = new Dictionary<string, string>();
        DecodeLineGroup(primaryBuffer, "DEDLines", (int)Marshal.OffsetOf<BMS4FlightDataNative>("DEDLines"), invert: false, result);
        DecodeLineGroup(primaryBuffer, "Invert", (int)Marshal.OffsetOf<BMS4FlightDataNative>("Invert"), invert: true, result);
        DecodeLineGroup(primaryBuffer, "PFLLines", (int)Marshal.OffsetOf<BMS4FlightDataNative>("PFLLines"), invert: false, result);
        DecodeLineGroup(primaryBuffer, "PFLInvert", (int)Marshal.OffsetOf<BMS4FlightDataNative>("PFLInvert"), invert: true, result);
        return result;
    }

    private static void DecodeLineGroup(ReadOnlySpan<byte> buffer, string name, int baseOffset, bool invert, Dictionary<string, string> result)
    {
        for (int line = 0; line < DedPflLineCount; line++)
        {
            int offset = baseOffset + line * DedPflLineLength;
            if (offset < 0 || offset + DedPflLineLength > buffer.Length)
            {
                continue;
            }

            var sb = new StringBuilder(DedPflLineLength);
            for (int i = 0; i < DedPflLineLength; i++)
            {
                byte b = buffer[offset + i];
                if (invert)
                {
                    sb.Append(b == 0x02 ? (char)b : ' ');
                }
                else if (b != 0)
                {
                    sb.Append((char)b);
                }
            }
            result[$"{name}_{line}"] = sb.ToString();
        }
    }

    private static IEnumerable<Falcon4FieldDefinition> ScalarFieldsFrom<T>() where T : struct
    {
        foreach (var fi in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (fi.FieldType.IsArray) continue; // handled explicitly by the caller (RWR/bit arrays etc.)

            Falcon4FieldType? type = MapClrType(fi.FieldType);
            if (type is null) continue;

            int offset = (int)Marshal.OffsetOf<T>(fi.Name);
            yield return new Falcon4FieldDefinition
            {
                Name = fi.Name,
                ByteOffset = offset,
                Type = type.Value,
                Scale = 1.0,
                Offset = 0.0,
                Notes = $"F4SharedMem {typeof(T).Name.Replace("Native", string.Empty)}.{fi.Name} ({fi.FieldType.Name}) - native units, see lightningstools header comments."
            };
        }
    }

    private static Falcon4FieldType? MapClrType(Type t)
    {
        if (t.IsEnum) t = Enum.GetUnderlyingType(t);

        if (t == typeof(float)) return Falcon4FieldType.Float32;
        if (t == typeof(int)) return Falcon4FieldType.Int32;
        if (t == typeof(uint)) return Falcon4FieldType.UInt32;
        if (t == typeof(short)) return Falcon4FieldType.Int16;
        if (t == typeof(ushort)) return Falcon4FieldType.UInt16;
        if (t == typeof(byte)) return Falcon4FieldType.Byte;
        if (t == typeof(sbyte)) return Falcon4FieldType.SByte;
        return null;
    }

    private static void AddIndexedArray(List<Falcon4FieldDefinition> fields, string name, int baseOffset, int elementSize, int count, Falcon4FieldType type, string note)
    {
        for (int i = 0; i < count; i++)
        {
            fields.Add(new Falcon4FieldDefinition
            {
                Name = $"{name}_{i}",
                ByteOffset = baseOffset + i * elementSize,
                Type = type,
                Scale = 1.0,
                Offset = 0.0,
                Notes = $"{note} (index {i})."
            });
        }
    }

    /// <summary>Reflects over a vendored [Flags] enum and emits one Bit-type field per single-bit member (composite "AllXxxOn"-style aggregate members are skipped since they aren't a power of two).</summary>
    private static void AddBitFlags<TEnum>(List<Falcon4FieldDefinition> fields, string sourceFieldName, int baseByteOffset) where TEnum : struct, Enum
    {
        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            uint bits = Convert.ToUInt32(value);
            if (bits == 0 || (bits & (bits - 1)) != 0) continue; // skip zero / composite (non-single-bit) members

            int bitPos = System.Numerics.BitOperations.Log2(bits);
            fields.Add(new Falcon4FieldDefinition
            {
                Name = $"{sourceFieldName}_{value}",
                ByteOffset = baseByteOffset + bitPos / 8,
                BitIndex = bitPos % 8,
                Type = Falcon4FieldType.Bit,
                Notes = $"Bit {bitPos} of {sourceFieldName} ({typeof(TEnum).Name.Replace("Native", string.Empty)}.{value})."
            });
        }
    }
}
