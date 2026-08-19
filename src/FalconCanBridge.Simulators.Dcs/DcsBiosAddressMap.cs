using System;
using System.Collections.Generic;
using System.Text;

namespace FalconCanBridge.Simulators.Dcs;

/// <summary>
/// Maintains a flat 64 KiB image of the DCS-BIOS export memory (addresses are UInt16, so this
/// comfortably covers the whole address space) and decodes configured
/// <see cref="DcsBiosSignalDefinition"/> entries out of it on demand.
/// </summary>
public sealed class DcsBiosAddressMap
{
    private readonly byte[] _memory = new byte[65536];
    public List<DcsBiosSignalDefinition> Fields { get; set; } = new();

    public void ApplyUpdate(DcsBiosUpdate update)
    {
        int end = Math.Min(update.Data.Length, _memory.Length - update.Address);
        Array.Copy(update.Data, 0, _memory, update.Address, Math.Max(0, end));
    }

    public Dictionary<string, double> DecodeNumbers()
    {
        var result = new Dictionary<string, double>(Fields.Count);

        foreach (var f in Fields)
        {
            if (f.Kind != DcsBiosSignalKind.Number) continue;
            if (f.Address + 1 >= _memory.Length) continue;

            ushort word = (ushort)(_memory[f.Address] | (_memory[f.Address + 1] << 8));
            ushort masked = (ushort)(word & f.Mask);
            double raw = f.ShiftBy > 0 ? masked >> f.ShiftBy : masked;
            result[f.Name] = raw * f.Scale + f.Offset;
        }

        return result;
    }

    public Dictionary<string, string> DecodeStrings()
    {
        var result = new Dictionary<string, string>();

        foreach (var f in Fields)
        {
            if (f.Kind != DcsBiosSignalKind.String) continue;
            int length = Math.Max(0, Math.Min(f.StringLength, _memory.Length - f.Address));
            if (length == 0) continue;

            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                byte b = _memory[f.Address + i];
                sb.Append(b == 0 ? ' ' : (char)b);
            }
            result[f.Name] = sb.ToString();
        }

        return result;
    }
}
