using System;
using System.Collections.Generic;

namespace FalconCanBridge.Core.Models;

/// <summary>
/// A bag of named telemetry values produced by a simulator connector.
/// Signal names are simulator-specific identifiers (e.g. "AirspeedKts", "AltitudeFt",
/// "GearHandle") that are referenced from <see cref="SignalMapping"/> entries.
/// Numeric values are stored as double for simplicity; boolean/discrete signals use 0/1.
/// </summary>
public sealed class TelemetrySnapshot
{
    public string SourceName { get; }

    public DateTime Timestamp { get; }

    public IReadOnlyDictionary<string, double> Values { get; }

    /// <summary>Optional free-text values (radio text, PFL lines, etc.) not used for CAN encoding.</summary>
    public IReadOnlyDictionary<string, string>? TextValues { get; }

    public TelemetrySnapshot(string sourceName, IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, string>? textValues = null)
    {
        SourceName = sourceName;
        Timestamp = DateTime.Now;
        Values = values;
        TextValues = textValues;
    }

    public bool TryGet(string signalName, out double value) => Values.TryGetValue(signalName, out value);

    public double GetOrDefault(string signalName, double defaultValue = 0.0)
        => Values.TryGetValue(signalName, out double v) ? v : defaultValue;
}

public sealed class TelemetryUpdatedEventArgs : EventArgs
{
    public TelemetrySnapshot Snapshot { get; }

    public TelemetryUpdatedEventArgs(TelemetrySnapshot snapshot) => Snapshot = snapshot;
}
