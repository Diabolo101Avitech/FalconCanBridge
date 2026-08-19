using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Simulators.Dcs;

/// <summary>
/// <see cref="ISimulatorConnector"/> for DCS World via DCS-BIOS.
///
/// This talks to the DCS-BIOS export/command UDP protocol, NOT to DCS itself - the user must
/// install the DCS-BIOS Lua hooks into their Saved Games\DCS\Scripts\Export.lua (see
/// docs/DCS_SETUP.md for the one-line snippet DCS-BIOS's own installer adds). Once that's in
/// place, DCS streams cockpit state to UDP multicast 239.255.50.10:5010 by default (DCS-BIOS's
/// documented default), and accepts plaintext "&lt;CONTROL&gt; &lt;VALUE&gt;\n" command packets
/// on UDP port 7778.
/// </summary>
public sealed class DcsBiosConnector : ISimulatorConnector
{
    private const string LogSource = "DCS-BIOS";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(3);

    private readonly string _listenAddress;
    private readonly int _listenPort;
    private readonly string _multicastGroup;
    private readonly string _commandHost;
    private readonly int _commandPort;
    private readonly DcsBiosAddressMap _addressMap;
    private readonly DcsBiosProtocolParser _parser = new();
    private readonly TimeSpan _minEmitInterval;

    private UdpClient? _listenClient;
    private UdpClient? _commandClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _watchdogTask;

    private DateTime _lastDataReceived = DateTime.MinValue;
    private DateTime _lastEmit = DateTime.MinValue;
    private bool _isConnected;

    public string Name => "DCS World (DCS-BIOS)";

    public SimulatorTarget Target => SimulatorTarget.Dcs;

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

    public DcsBiosConnector(
        DcsBiosAddressMap addressMap,
        string listenAddress = "0.0.0.0",
        int listenPort = 5010,
        string multicastGroup = "239.255.50.10",
        string commandHost = "127.0.0.1",
        int commandPort = 7778,
        int maxEmitRateHz = 30)
    {
        _addressMap = addressMap;
        _listenAddress = listenAddress;
        _listenPort = listenPort;
        _multicastGroup = multicastGroup;
        _commandHost = commandHost;
        _commandPort = commandPort;
        _minEmitInterval = maxEmitRateHz > 0 ? TimeSpan.FromSeconds(1.0 / maxEmitRateHz) : TimeSpan.Zero;

        _parser.UpdateParsed += update => _addressMap.ApplyUpdate(update);
        _parser.FrameSync += OnFrameSync;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveTask is not null) return Task.CompletedTask;

        _listenClient = new UdpClient();
        _listenClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listenClient.Client.Bind(new IPEndPoint(IPAddress.Parse(_listenAddress), _listenPort));

        try
        {
            _listenClient.JoinMulticastGroup(IPAddress.Parse(_multicastGroup));
            Log($"Listening on {_listenAddress}:{_listenPort}, joined multicast group {_multicastGroup}.");
        }
        catch (Exception ex)
        {
            Log($"Could not join multicast group {_multicastGroup} ({ex.Message}) - still listening for unicast export data on port {_listenPort}.", LogLevel.Warning);
        }

        _commandClient = new UdpClient();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
        _watchdogTask = Task.Run(() => WatchdogLoop(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();

        _listenClient?.Close();
        _commandClient?.Close();

        try
        {
            if (_receiveTask is not null) await _receiveTask;
            if (_watchdogTask is not null) await _watchdogTask;
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            _receiveTask = null;
            _watchdogTask = null;
            _cts.Dispose();
            _cts = null;
            _listenClient = null;
            _commandClient = null;
            IsConnected = false;
        }
    }

    /// <summary>Sends a DCS-BIOS control command, e.g. SendCommand("UFC_1", 1) then SendCommand("UFC_1", 0) for a momentary button.</summary>
    public void SendCommand(string commandName, double value)
    {
        if (_commandClient is null)
        {
            Log("Cannot send command, connector not started.", LogLevel.Warning);
            return;
        }

        string valueStr = value == Math.Truncate(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);

        string message = $"{commandName} {valueStr}\n";
        byte[] payload = Encoding.ASCII.GetBytes(message);

        try
        {
            _commandClient.Send(payload, payload.Length, _commandHost, _commandPort);
        }
        catch (Exception ex)
        {
            Log($"Failed to send command '{commandName}': {ex.Message}", LogLevel.Error);
        }
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        if (_listenClient is null) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _listenClient.ReceiveAsync(token);
                _lastDataReceived = DateTime.Now;
                _parser.Feed(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Receive error: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private async Task WatchdogLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            bool recentData = _lastDataReceived != DateTime.MinValue && (DateTime.Now - _lastDataReceived) < ConnectionTimeout;
            IsConnected = recentData;
        }
    }

    private void OnFrameSync()
    {
        var now = DateTime.Now;
        if (_minEmitInterval > TimeSpan.Zero && (now - _lastEmit) < _minEmitInterval) return;
        _lastEmit = now;

        var numbers = _addressMap.DecodeNumbers();
        var strings = _addressMap.DecodeStrings();
        var snapshot = new TelemetrySnapshot(Target.ToString(), numbers, strings);
        TelemetryUpdated?.Invoke(this, new TelemetryUpdatedEventArgs(snapshot));
    }

    private void Log(string message, LogLevel level = LogLevel.Info)
    {
        AppLog.Write(level, LogSource, message);
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
