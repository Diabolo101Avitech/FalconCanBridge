using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;
using FalconCanBridge.Simulators.Falcon4.Native;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// <see cref="ISimulatorConnector"/> implementation for Falcon 4 BMS. Polls BOTH the primary
/// "FalconSharedMemoryArea" block (basic flight/attitude/systems data) and the secondary
/// "FalconSharedMemoryArea2" block (added over later BMS4 versions - navigation/power/ECM/IFF/...)
/// on a background loop (BMS itself refreshes them once per sim frame, so polling at 20-30 Hz is
/// more than enough for gauges/lights) and raises <see cref="TelemetryUpdated"/> with the decoded
/// named signals from both, merged into one snapshot. Retries opening the primary mapping
/// indefinitely so the app can be started before or after BMS; the secondary mapping is opened
/// best-effort and simply contributes nothing if unavailable (older BMS builds, or not yet
/// refreshed this tick) without affecting <see cref="IsConnected"/>, which tracks the primary block
/// only.
/// </summary>
public sealed class Falcon4Connector : ISimulatorConnector
{
    private const string LogSource = "Falcon4";

    private readonly Falcon4SharedMemoryReader _reader = new();
    private readonly Falcon4SharedMemoryReader _secondaryReader = new(Falcon4SharedMemoryReader.SecondaryMemoryMappedFileName);
    private readonly Falcon4FieldMap _fieldMap;
    private readonly Falcon4FieldMap _secondaryFieldMap;
    private readonly int _pollIntervalMs;
    private readonly int _bufferSize;
    private Falcon4KeyboardCommandSender? _commandSender;
    private bool _loggedSecondaryCollision;

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _isConnected;

    public string Name => "Falcon 4 BMS";

    public SimulatorTarget Target => SimulatorTarget.Falcon4Bms;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<TelemetryUpdatedEventArgs>? TelemetryUpdated;
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler<string>? LogMessage;

    public Falcon4Connector(Falcon4FieldMap? fieldMap = null, Falcon4FieldMap? secondaryFieldMap = null, int pollIntervalMs = 33, int bufferSize = 65536)
    {
        _fieldMap = fieldMap ?? new Falcon4FieldMap();
        _secondaryFieldMap = secondaryFieldMap ?? Falcon4FieldMap.CreateSecondaryDefault();
        _pollIntervalMs = pollIntervalMs;
        _bufferSize = bufferSize;
    }

    /// <summary>Wires up keyboard-emulation command sending for CanToSim mappings targeting BMS. Optional - without it, SendCommand is a no-op.</summary>
    public void ConfigureKeyBindings(IEnumerable<Falcon4KeyBinding> bindings)
    {
        _commandSender = new Falcon4KeyboardCommandSender(bindings);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pollTask is not null) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = Task.Run(() => PollLoop(_cts.Token), _cts.Token);
        Log($"Started, polling every {_pollIntervalMs} ms.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try
        {
            if (_pollTask is not null) await _pollTask;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _pollTask = null;
            _cts.Dispose();
            _cts = null;
            _reader.Close();
            _secondaryReader.Close();
            IsConnected = false;
        }
    }

    public void SendCommand(string commandName, double value)
    {
        if (_commandSender is null)
        {
            Log($"SendCommand('{commandName}') ignored: no key bindings configured.", LogLevel.Warning);
            return;
        }
        _commandSender.Send(commandName, value);
    }

    private async Task PollLoop(CancellationToken token)
    {
        byte[] buffer = new byte[_bufferSize];
        byte[] secondaryBuffer = new byte[_bufferSize];

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_reader.IsOpen)
                {
                    if (_reader.TryOpen())
                    {
                        Log($"Opened primary shared memory mapping ({_reader.Capacity} bytes).");
                        IsConnected = true;
                    }
                    else
                    {
                        IsConnected = false;
                        await Task.Delay(1000, token);
                        continue;
                    }
                }

                int read = _reader.ReadSnapshot(buffer);
                if (read == 0)
                {
                    // Mapping went away (BMS exited / mission ended) - close and retry.
                    Log("Lost primary shared memory mapping, will retry.", LogLevel.Warning);
                    _reader.Close();
                    IsConnected = false;
                    await Task.Delay(1000, token);
                    continue;
                }

                var primarySpan = buffer.AsSpan(0, read);
                var values = _fieldMap.Decode(primarySpan);
                var textValues = Falcon4NativeFieldMapBuilder.DecodeDedPflText(primarySpan);

                // Secondary block ("FalconSharedMemoryArea2") is best-effort: older BMS builds
                // don't publish it, and a transient miss here shouldn't affect IsConnected (which
                // tracks the primary block only) or interrupt the primary telemetry snapshot.
                if (_secondaryReader.IsOpen || _secondaryReader.TryOpen())
                {
                    int read2 = _secondaryReader.ReadSnapshot(secondaryBuffer);
                    if (read2 > 0)
                    {
                        var secondaryValues = _secondaryFieldMap.Decode(secondaryBuffer.AsSpan(0, read2));
                        MergeSecondaryValues(values, secondaryValues);
                    }
                    else
                    {
                        _secondaryReader.Close();
                    }
                }

                var snapshot = new TelemetrySnapshot(Target.ToString(), values, textValues);
                TelemetryUpdated?.Invoke(this, new TelemetryUpdatedEventArgs(snapshot));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Poll loop error: {ex.Message}", LogLevel.Error);
                await Task.Delay(1000, token);
                continue;
            }

            await Task.Delay(_pollIntervalMs, token);
        }
    }

    /// <summary>
    /// Merges secondary-block values into the primary dictionary. The two blocks' field names are
    /// distinct in almost every case, but a couple of native field names collide by coincidence
    /// (e.g. both structs have their own "VersionNum2") - on collision the secondary value is kept
    /// under a "_Fd2" suffix instead of silently overwriting the primary one, and this is logged
    /// once so it doesn't get lost.
    /// </summary>
    private void MergeSecondaryValues(Dictionary<string, double> primary, Dictionary<string, double> secondary)
    {
        foreach (var kvp in secondary)
        {
            if (primary.ContainsKey(kvp.Key))
            {
                if (!_loggedSecondaryCollision)
                {
                    Log($"Secondary field '{kvp.Key}' has the same name as a primary field - keeping it as '{kvp.Key}_Fd2'.", LogLevel.Warning);
                    _loggedSecondaryCollision = true;
                }
                primary[$"{kvp.Key}_Fd2"] = kvp.Value;
            }
            else
            {
                primary[kvp.Key] = kvp.Value;
            }
        }
    }

    private void Log(string message, LogLevel level = LogLevel.Info)
    {
        AppLog.Write(level, LogSource, message);
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _reader.Dispose();
        _secondaryReader.Dispose();
    }
}
