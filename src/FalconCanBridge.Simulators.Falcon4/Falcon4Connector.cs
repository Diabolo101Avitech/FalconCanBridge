using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// <see cref="ISimulatorConnector"/> implementation for Falcon 4 BMS. Polls the
/// "FalconSharedMemoryArea" shared-memory block on a background loop (BMS itself refreshes it
/// once per sim frame, so polling at 20-30 Hz is more than enough for gauges/lights) and raises
/// <see cref="TelemetryUpdated"/> with the decoded named signals. Retries opening the mapping
/// indefinitely so the app can be started before or after BMS.
/// </summary>
public sealed class Falcon4Connector : ISimulatorConnector
{
    private const string LogSource = "Falcon4";

    private readonly Falcon4SharedMemoryReader _reader = new();
    private readonly Falcon4FieldMap _fieldMap;
    private readonly int _pollIntervalMs;
    private readonly int _bufferSize;
    private Falcon4KeyboardCommandSender? _commandSender;

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

    public Falcon4Connector(Falcon4FieldMap? fieldMap = null, int pollIntervalMs = 33, int bufferSize = 65536)
    {
        _fieldMap = fieldMap ?? new Falcon4FieldMap();
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

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_reader.IsOpen)
                {
                    if (_reader.TryOpen())
                    {
                        Log($"Opened shared memory mapping ({_reader.Capacity} bytes).");
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
                    Log("Lost shared memory mapping, will retry.", LogLevel.Warning);
                    _reader.Close();
                    IsConnected = false;
                    await Task.Delay(1000, token);
                    continue;
                }

                var values = _fieldMap.Decode(buffer.AsSpan(0, read));
                var snapshot = new TelemetrySnapshot(Target.ToString(), values);
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

    private void Log(string message, LogLevel level = LogLevel.Info)
    {
        AppLog.Write(level, LogSource, message);
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _reader.Dispose();
    }
}
