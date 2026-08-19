using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FalconCanBridge.Simulators.Dcs;

public enum DcsBiosSignalKind
{
    /// <summary>A single 16-bit word (with optional mask/shift for sub-fields), scaled to an engineering value.</summary>
    Number,
    /// <summary>A run of consecutive ASCII bytes (PFL/UFC scratchpad lines, frequency readouts, etc.).</summary>
    String
}

/// <summary>
/// Describes one named signal to extract from the DCS-BIOS export memory image.
///
/// DCS-BIOS assigns every cockpit control/output a byte <c>Address</c> (and, for bit/selector
/// fields packed into a shared word, a <c>Mask</c>/<c>ShiftBy</c>) that is specific to the
/// aircraft module. Those addresses are NOT invented here - they come from the official
/// per-aircraft "control reference" JSON exported by DCS-BIOS itself (visible in the built-in
/// DCS-BIOS Hub web UI under each module, or the json files under
/// "Saved Games\DCS\Scripts\DCS-BIOS\doc\json\&lt;aircraft&gt;.json" once DCS-BIOS is
/// installed). Copy the address/mask/shift for the specific controls you want onto your CAN
/// panel into config/dcs-bios-fields.json.
/// </summary>
public sealed class DcsBiosSignalDefinition
{
    public string Name { get; set; } = string.Empty;

    public DcsBiosSignalKind Kind { get; set; } = DcsBiosSignalKind.Number;

    /// <summary>Byte offset into the export memory image.</summary>
    public ushort Address { get; set; }

    /// <summary>Applied to the little-endian 16-bit word read at Address before ShiftBy/Scale/Offset. Ignored for String kind.</summary>
    public ushort Mask { get; set; } = 0xFFFF;

    public int ShiftBy { get; set; }

    public double Scale { get; set; } = 1.0;

    public double Offset { get; set; }

    /// <summary>String kind only: number of ASCII bytes starting at Address.</summary>
    public int StringLength { get; set; }

    /// <summary>Free-text note, e.g. where the address came from or which aircraft it's valid for.</summary>
    public string Notes { get; set; } = string.Empty;

    public static List<DcsBiosSignalDefinition> LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        return JsonSerializer.Deserialize<List<DcsBiosSignalDefinition>>(json, options) ?? new List<DcsBiosSignalDefinition>();
    }

    public static void SaveToFile(string path, List<DcsBiosSignalDefinition> fields)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(fields, options));
    }
}
