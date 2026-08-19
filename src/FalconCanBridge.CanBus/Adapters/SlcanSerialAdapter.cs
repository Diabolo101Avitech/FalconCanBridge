using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.CanBus.Adapters;

/// <summary>
/// CAN adapter for an STM32-based USB-CDC "CAN interface" board running the SLCAN (a.k.a.
/// LAWICEL CANUSB) ASCII-over-serial protocol - the de-facto open standard implemented by most
/// hobbyist/open-source STM32 CAN-USB firmwares (e.g. candleLight/CANable-style slcan builds)
/// and natively understood by Linux SocketCAN's `slcand`. This is the recommended path for a
/// custom STM32 board: implement SLCAN in firmware and this adapter talks to it with zero
/// vendor SDKs.
///
/// Frame commands (all terminated with CR, 0x0D):
///   t&lt;id:3hex&gt;&lt;dlc:1hex&gt;&lt;data:dlc*2hex&gt;   standard data frame
///   T&lt;id:8hex&gt;&lt;dlc:1hex&gt;&lt;data:dlc*2hex&gt;   extended data frame
///   r&lt;id:3hex&gt;&lt;dlc:1hex&gt;                        standard remote frame
///   R&lt;id:8hex&gt;&lt;dlc:1hex&gt;                        extended remote frame
/// Control commands: S&lt;n&gt; set bitrate preset, O open channel, C close channel.
/// A bell character (0x07) from the device signals a rejected/malformed command.
///
/// connectionString format: "&lt;COM port&gt;;&lt;CAN bitrate bps&gt;;&lt;serial baud&gt;",
/// e.g. "COM5;500000;115200". CAN bitrate must be one of the standard SLCAN presets
/// (10000/20000/50000/100000/125000/250000/500000/800000/1000000).
/// </summary>
public sealed class SlcanSerialAdapter : ICanBusAdapter
{
    private const string LogSource = "SLCAN";

    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly StringBuilder _lineBuffer = new();

    public string Name => "SLCAN (STM32 USB-CDC)";

    public bool IsOpen => _port?.IsOpen == true;

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<CanFrameTransmittedEventArgs>? FrameTransmitted;
    public event EventHandler<string>? LogMessage;
    public event EventHandler? ConnectionStateChanged;

    public async Task OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var (portName, canBitrate, serialBaud) = ParseConnectionString(connectionString);
        string bitratePreset = BitrateToPreset(canBitrate);

        _port = new SerialPort(portName, serialBaud)
        {
            NewLine = "\r",
            ReadTimeout = 2000,
            WriteTimeout = 2000
        };
        _port.Open();

        // Best-effort reset: close any previously-open channel before reconfiguring bitrate.
        WriteRaw("C\r");
        await Task.Delay(50, cancellationToken);
        WriteRaw($"{bitratePreset}\r");
        await Task.Delay(50, cancellationToken);
        WriteRaw("O\r");
        await Task.Delay(50, cancellationToken);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);

        Log($"Opened {portName} @ {serialBaud} baud, CAN bitrate {canBitrate} bps ({bitratePreset}).");
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CloseAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            try { if (_readTask is not null) await _readTask; }
            catch (OperationCanceledException) { }
            _cts.Dispose();
            _cts = null;
            _readTask = null;
        }

        if (_port is { IsOpen: true })
        {
            try { WriteRaw("C\r"); } catch { /* best effort */ }
            _port.Close();
        }
        _port = null;

        Log("Closed.");
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        if (_port is not { IsOpen: true })
        {
            Log("Cannot send, port not open.", LogLevel.Warning);
            return Task.CompletedTask;
        }

        char cmd = frame.IsExtended ? 'T' : 't';
        string idHex = frame.IsExtended ? frame.Id.ToString("X8") : (frame.Id & 0x7FF).ToString("X3");
        string dlcHex = frame.Dlc.ToString("X1");
        string dataHex = Convert.ToHexString(frame.Data);

        string line = $"{cmd}{idHex}{dlcHex}{dataHex}\r";
        WriteRaw(line);
        FrameTransmitted?.Invoke(this, new CanFrameTransmittedEventArgs(frame));

        return Task.CompletedTask;
    }

    private void WriteRaw(string s)
    {
        _port?.Write(s);
    }

    private void ReadLoop(CancellationToken token)
    {
        if (_port is null) return;

        while (!token.IsCancellationRequested)
        {
            int b;
            try
            {
                b = _port.ReadByte();
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
            {
                // Port closed / unplugged.
                break;
            }

            if (b < 0) continue;

            if (b == '\r')
            {
                string line = _lineBuffer.ToString();
                _lineBuffer.Clear();
                if (line.Length > 0) TryParseLine(line);
            }
            else if (b == 0x07)
            {
                Log("Adapter reported an error (BEL) for the last command.", LogLevel.Warning);
                _lineBuffer.Clear();
            }
            else
            {
                _lineBuffer.Append((char)b);
            }
        }
    }

    private void TryParseLine(string line)
    {
        try
        {
            char cmd = line[0];
            bool extended = cmd is 'T' or 'R';
            bool isRemote = cmd is 'r' or 'R';
            if (cmd is not ('t' or 'T' or 'r' or 'R')) return;

            int idLen = extended ? 8 : 3;
            uint id = Convert.ToUInt32(line.Substring(1, idLen), 16);
            int dlc = Convert.ToInt32(line.Substring(1 + idLen, 1), 16);

            byte[] data = Array.Empty<byte>();
            if (!isRemote && dlc > 0)
            {
                int dataStart = 1 + idLen + 1;
                string dataHex = line.Substring(dataStart, dlc * 2);
                data = Convert.FromHexString(dataHex);
            }

            var frame = new CanFrame(id, data, extended, CanFrameDirection.Rx) { IsRemoteRequest = isRemote };
            FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
        }
        catch (Exception ex)
        {
            Log($"Failed to parse received line '{line}': {ex.Message}", LogLevel.Warning);
        }
    }

    private static (string portName, int canBitrate, int serialBaud) ParseConnectionString(string s)
    {
        string[] parts = s.Split(';', StringSplitOptions.TrimEntries);
        string portName = parts.Length > 0 ? parts[0] : "COM1";
        int canBitrate = parts.Length > 1 && int.TryParse(parts[1], out int br) ? br : 500000;
        int serialBaud = parts.Length > 2 && int.TryParse(parts[2], out int sb) ? sb : 115200;
        return (portName, canBitrate, serialBaud);
    }

    private static string BitrateToPreset(int bitrate) => bitrate switch
    {
        10000 => "S0",
        20000 => "S1",
        50000 => "S2",
        100000 => "S3",
        125000 => "S4",
        250000 => "S5",
        500000 => "S6",
        800000 => "S7",
        1000000 => "S8",
        _ => "S6" // default to 500 kbit/s
    };

    private void Log(string message, LogLevel level = LogLevel.Info)
    {
        AppLog.Write(level, LogSource, message);
        LogMessage?.Invoke(this, message);
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
