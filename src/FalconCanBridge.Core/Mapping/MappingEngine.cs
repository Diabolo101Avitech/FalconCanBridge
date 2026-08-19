using System;
using System.Collections.Generic;
using System.Linq;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.Mapping;

public sealed class SimCommandRequestedEventArgs : EventArgs
{
    public SimulatorTarget Target { get; }
    public string CommandName { get; }
    public double Value { get; }

    public SimCommandRequestedEventArgs(SimulatorTarget target, string commandName, double value)
    {
        Target = target;
        CommandName = commandName;
        Value = value;
    }
}

/// <summary>
/// Bidirectional translation core of the bridge.
///
/// SimToCan: consumes <see cref="TelemetrySnapshot"/> events from a simulator connector,
/// packs the configured signals into per-CAN-ID byte buffers (several signals can share a
/// frame), and raises <see cref="CanFrameReady"/> when a frame should be (re)transmitted -
/// throttled by <see cref="SignalMapping.SendRateMs"/> and gated by
/// <see cref="SignalMapping.ChangeThreshold"/> so the bus isn't saturated by high-rate values.
///
/// CanToSim: consumes received <see cref="CanFrame"/>s from the CAN adapter, decodes the
/// configured fields, and raises <see cref="SimCommandRequested"/> so the host application can
/// route the resulting command to the correct simulator connector.
/// </summary>
public sealed class MappingEngine
{
    private const string LogSource = "MappingEngine";

    private sealed class OutputFrameState
    {
        public readonly byte[] Buffer = new byte[8];
        public int Length;
        public bool ExtendedId;
        public DateTime LastSent = DateTime.MinValue;
        public readonly Dictionary<string, double> LastRawByMappingId = new();
    }

    private readonly object _lock = new();
    private List<SignalMapping> _mappings = new();
    private Dictionary<uint, List<SignalMapping>> _outputMappingsByCanId = new();
    private Dictionary<uint, List<SignalMapping>> _inputMappingsByCanId = new();
    private readonly Dictionary<uint, OutputFrameState> _outputFrameState = new();

    public event EventHandler<CanFrame>? CanFrameReady;
    public event EventHandler<SimCommandRequestedEventArgs>? SimCommandRequested;

    public IReadOnlyList<SignalMapping> Mappings
    {
        get { lock (_lock) { return _mappings.ToList(); } }
    }

    public void LoadMappings(IEnumerable<SignalMapping> mappings)
    {
        lock (_lock)
        {
            _mappings = mappings.ToList();

            _outputMappingsByCanId = _mappings
                .Where(m => m.Direction == MappingDirection.SimToCan)
                .GroupBy(m => m.CanId)
                .ToDictionary(g => g.Key, g => g.ToList());

            _inputMappingsByCanId = _mappings
                .Where(m => m.Direction == MappingDirection.CanToSim)
                .GroupBy(m => m.CanId)
                .ToDictionary(g => g.Key, g => g.ToList());

            _outputFrameState.Clear();
            foreach (var (canId, group) in _outputMappingsByCanId)
            {
                int length = group.Where(m => m.Enabled).Select(m => m.ByteOffset + m.ByteWidth).DefaultIfEmpty(1).Max();
                length = Math.Clamp(length, 1, 8);
                _outputFrameState[canId] = new OutputFrameState
                {
                    Length = length,
                    ExtendedId = group.First().ExtendedId
                };
            }
        }

        AppLog.Info(LogSource, $"Loaded {_mappings.Count} mapping(s): {_outputMappingsByCanId.Count} output frame(s), {_inputMappingsByCanId.Count} input frame(s).");
    }

    /// <summary>Feed a fresh telemetry snapshot from a simulator connector. May raise <see cref="CanFrameReady"/> zero or more times.</summary>
    public void OnTelemetry(TelemetrySnapshot snapshot)
    {
        var framesToSend = new List<CanFrame>();
        var now = DateTime.Now;

        lock (_lock)
        {
            foreach (var (canId, group) in _outputMappingsByCanId)
            {
                if (!_outputFrameState.TryGetValue(canId, out var state))
                {
                    continue;
                }

                bool changed = false;

                foreach (var m in group)
                {
                    if (!m.Enabled) continue;
                    if (m.Target != SimulatorTarget.Any && !MatchesTarget(m.Target, snapshot.SourceName)) continue;
                    if (!snapshot.TryGet(m.SignalName, out double engValue)) continue;

                    double clampedEng = Math.Clamp(engValue, m.MinValue, m.MaxValue);
                    double scale = m.Scale == 0 ? 1.0 : m.Scale;
                    double raw = (clampedEng - m.Offset) / scale;

                    WriteValueToBuffer(state.Buffer, m, raw);

                    bool hadPrevious = state.LastRawByMappingId.TryGetValue(m.Id, out double prevRaw);
                    if (!hadPrevious || Math.Abs(raw - prevRaw) >= m.ChangeThreshold)
                    {
                        changed = true;
                    }
                    state.LastRawByMappingId[m.Id] = raw;
                }

                if (!changed) continue;

                int minRateMs = group.Where(m => m.Enabled).Select(m => m.SendRateMs).DefaultIfEmpty(50).Min();
                if ((now - state.LastSent).TotalMilliseconds < minRateMs) continue;

                state.LastSent = now;
                byte[] payload = new byte[state.Length];
                Array.Copy(state.Buffer, payload, state.Length);
                framesToSend.Add(new CanFrame(canId, payload, state.ExtendedId, CanFrameDirection.Tx));
            }
        }

        foreach (var frame in framesToSend)
        {
            CanFrameReady?.Invoke(this, frame);
        }
    }

    /// <summary>Feed a CAN frame received from the STM32 bus. May raise <see cref="SimCommandRequested"/> zero or more times.</summary>
    public void OnCanFrameReceived(CanFrame frame)
    {
        List<SimCommandRequestedEventArgs>? commands = null;

        lock (_lock)
        {
            if (!_inputMappingsByCanId.TryGetValue(frame.Id, out var list)) return;

            byte[] buffer = new byte[8];
            Array.Copy(frame.Data, buffer, Math.Min(8, frame.Data.Length));

            foreach (var m in list)
            {
                if (!m.Enabled) continue;

                double raw = ReadValueFromBuffer(buffer, m);
                double eng = raw * m.Scale + m.Offset;
                eng = Math.Clamp(eng, m.MinValue, m.MaxValue);

                string command = string.IsNullOrWhiteSpace(m.CommandName) ? m.SignalName : m.CommandName;
                commands ??= new List<SimCommandRequestedEventArgs>();
                commands.Add(new SimCommandRequestedEventArgs(m.Target, command, eng));
            }
        }

        if (commands is null) return;
        foreach (var cmd in commands)
        {
            SimCommandRequested?.Invoke(this, cmd);
        }
    }

    private static bool MatchesTarget(SimulatorTarget target, string sourceName)
        => string.Equals(target.ToString(), sourceName, StringComparison.OrdinalIgnoreCase);

    // ---- Bit/byte packing helpers -------------------------------------------------

    private static void WriteValueToBuffer(byte[] buffer, SignalMapping m, double rawValue)
    {
        int offset = Math.Clamp(m.ByteOffset, 0, 7);

        switch (m.DataType)
        {
            case CanDataType.Bit:
            {
                int bitPos = Math.Clamp(m.BitOffset, 0, 7);
                byte mask = (byte)(1 << bitPos);
                if (rawValue != 0)
                    buffer[offset] |= mask;
                else
                    buffer[offset] &= (byte)~mask;
                break;
            }
            case CanDataType.UInt8:
                buffer[offset] = (byte)Math.Clamp(rawValue, 0, 255);
                break;
            case CanDataType.Int8:
                buffer[offset] = unchecked((byte)(sbyte)Math.Clamp(rawValue, sbyte.MinValue, sbyte.MaxValue));
                break;
            case CanDataType.UInt16:
                WriteBytes(buffer, offset, BitConverter.GetBytes((ushort)Math.Clamp(rawValue, 0, ushort.MaxValue)), m.LittleEndian);
                break;
            case CanDataType.Int16:
                WriteBytes(buffer, offset, BitConverter.GetBytes((short)Math.Clamp(rawValue, short.MinValue, short.MaxValue)), m.LittleEndian);
                break;
            case CanDataType.UInt32:
                WriteBytes(buffer, offset, BitConverter.GetBytes((uint)Math.Clamp(rawValue, 0, uint.MaxValue)), m.LittleEndian);
                break;
            case CanDataType.Int32:
                WriteBytes(buffer, offset, BitConverter.GetBytes((int)Math.Clamp(rawValue, int.MinValue, int.MaxValue)), m.LittleEndian);
                break;
            case CanDataType.Float32:
                WriteBytes(buffer, offset, BitConverter.GetBytes((float)rawValue), m.LittleEndian);
                break;
        }
    }

    private static double ReadValueFromBuffer(byte[] buffer, SignalMapping m)
    {
        int offset = Math.Clamp(m.ByteOffset, 0, 7);

        switch (m.DataType)
        {
            case CanDataType.Bit:
            {
                int bitPos = Math.Clamp(m.BitOffset, 0, 7);
                return (buffer[offset] & (1 << bitPos)) != 0 ? 1.0 : 0.0;
            }
            case CanDataType.UInt8:
                return buffer[offset];
            case CanDataType.Int8:
                return unchecked((sbyte)buffer[offset]);
            case CanDataType.UInt16:
                return ReadUInt(buffer, offset, 2, m.LittleEndian);
            case CanDataType.Int16:
                return unchecked((short)ReadUInt(buffer, offset, 2, m.LittleEndian));
            case CanDataType.UInt32:
                return ReadUInt(buffer, offset, 4, m.LittleEndian);
            case CanDataType.Int32:
                return unchecked((int)ReadUInt(buffer, offset, 4, m.LittleEndian));
            case CanDataType.Float32:
            {
                byte[] bytes = ExtractBytes(buffer, offset, 4, m.LittleEndian);
                return BitConverter.ToSingle(bytes, 0);
            }
            default:
                return 0;
        }
    }

    private static void WriteBytes(byte[] buffer, int offset, byte[] valueBytes, bool littleEndian)
    {
        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(valueBytes);
        }

        for (int i = 0; i < valueBytes.Length && offset + i < 8; i++)
        {
            buffer[offset + i] = valueBytes[i];
        }
    }

    private static byte[] ExtractBytes(byte[] buffer, int offset, int count, bool littleEndian)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = offset + i < buffer.Length ? buffer[offset + i] : (byte)0;
        }

        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    /// <summary>Reads an unsigned integer of 2 or 4 bytes, honoring the field's configured byte order.</summary>
    private static uint ReadUInt(byte[] buffer, int offset, int count, bool littleEndian)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = offset + i < buffer.Length ? buffer[offset + i] : (byte)0;
        }

        // bytes[] is currently in on-wire order (as configured by littleEndian); BitConverter needs host order.
        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(bytes);
        }

        return count switch
        {
            2 => BitConverter.ToUInt16(bytes, 0),
            4 => BitConverter.ToUInt32(bytes, 0),
            _ => 0
        };
    }
}
