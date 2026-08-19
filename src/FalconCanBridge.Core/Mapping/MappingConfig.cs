using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.Mapping;

/// <summary>
/// Serializable root document persisted to/from JSON on disk (see config/mapping.sample.json).
/// </summary>
public sealed class MappingDocument
{
    /// <summary>Free-form label shown in the UI title bar.</summary>
    public string ProfileName { get; set; } = "Default Profile";

    public List<SignalMapping> Mappings { get; set; } = new();
}

public static class MappingConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // SignalMapping.MinValue/MaxValue default to +/-Infinity (an "unbounded" clamp range),
        // which System.Text.Json refuses to write/read as plain JSON numbers unless this is set.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public static MappingDocument Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MappingDocument>(json, JsonOptions)
               ?? new MappingDocument();
    }

    public static void Save(string path, MappingDocument document)
    {
        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json);
    }
}
