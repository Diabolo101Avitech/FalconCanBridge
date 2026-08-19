using System;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.Interfaces;

/// <summary>
/// Common contract implemented by each flight-sim data source (Falcon 4 BMS shared memory,
/// DCS-BIOS UDP export, ...). Connectors are push-based: once started they raise
/// <see cref="TelemetryUpdated"/> as new data arrives from the sim.
/// </summary>
public interface ISimulatorConnector : IDisposable
{
    /// <summary>Short display name, e.g. "Falcon 4 BMS" or "DCS World".</summary>
    string Name { get; }

    /// <summary>Which <see cref="SimulatorTarget"/> this connector represents.</summary>
    SimulatorTarget Target { get; }

    bool IsConnected { get; }

    event EventHandler<TelemetryUpdatedEventArgs>? TelemetryUpdated;

    event EventHandler? ConnectionStateChanged;

    /// <summary>Human-readable status/error messages for the UI log.</summary>
    event EventHandler<string>? LogMessage;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>
    /// Sends a command/input back into the simulator (e.g. trigger a BMS keybinding or a
    /// DCS-BIOS control). <paramref name="value"/> semantics are command-specific:
    /// for a momentary button, 1 = press, 0 = release; for a rotary, an analog value 0..1 etc.
    /// </summary>
    void SendCommand(string commandName, double value);
}
