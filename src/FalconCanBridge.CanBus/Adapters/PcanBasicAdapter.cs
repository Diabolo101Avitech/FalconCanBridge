using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.CanBus.Adapters;

/// <summary>
/// Alternative CAN adapter for setups where the PC reaches the STM32 node(s) through a PEAK
/// PCAN-USB dongle rather than a custom STM32 USB-CDC/SLCAN bridge. Requires PEAK's
/// "PCANBasic.dll" (from the free PCAN-Basic API package) to be installed/on PATH - this
/// class only P/Invokes it, it does not ship the vendor DLL.
///
/// Channel/baudrate constant values below come from PCANBasic.h and have been stable across
/// PEAK's API for a long time, but double-check them against the PCANBasic.h shipped with
/// whatever PCAN-Basic version you install if you see CAN_ERROR_ILLPARAMTYPE at Initialize.
///
/// connectionString format: "&lt;channel&gt;;&lt;bitrate&gt;" e.g. "USB1;500000".
/// </summary>
public sealed class PcanBasicAdapter : ICanBusAdapter
{
    private const string LogSource = "PCAN";
    private const string DllName = "PCANBasic.dll";

    public string Name => "PCAN-Basic (PEAK PCAN-USB)";

    public bool IsOpen { get; private set; }

    public event EventHandler<CanFrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<CanFrameTransmittedEventArgs>? FrameTransmitted;
    public event EventHandler<string>? LogMessage;
    public event EventHandler? ConnectionStateChanged;

    private ushort _channel;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public Task OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var (channel, bitrate) = ParseConnectionString(connectionString);
        _channel = channel;

        TPCANStatus status = CAN_Initialize(_channel, BitrateToEnum(bitrate), 0, 0, 0);
        if (status != TPCANStatus.PCAN_ERROR_OK)
        {
            throw new InvalidOperationException($"PCAN_Initialize failed with status 0x{(uint)status:X}.");
        }

        IsOpen = true;
        Log($"Initialized PCAN channel 0x{_channel:X} at {bitrate} bps.");
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = Task.Run(() => PollLoop(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            try { if (_pollTask is not null) await _pollTask; }
            catch (OperationCanceledException) { }
            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }

        if (IsOpen)
        {
            CAN_Uninitialize(_channel);
            IsOpen = false;
            Log("Uninitialized.");
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            Log("Cannot send, channel not open.", LogLevel.Warning);
            return Task.CompletedTask;
        }

        var msg = new TPCANMsg
        {
            ID = frame.Id,
            MSGTYPE = frame.IsExtended ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD,
            LEN = (byte)frame.Dlc,
            DATA = new byte[8]
        };
        Array.Copy(frame.Data, msg.DATA, frame.Dlc);

        TPCANStatus status = CAN_Write(_channel, ref msg);
        if (status != TPCANStatus.PCAN_ERROR_OK)
        {
            Log($"CAN_Write failed with status 0x{(uint)status:X}.", LogLevel.Warning);
        }
        else
        {
            FrameTransmitted?.Invoke(this, new CanFrameTransmittedEventArgs(frame));
        }

        return Task.CompletedTask;
    }

    private void PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TPCANStatus status = CAN_Read(_channel, out TPCANMsg msg, out _);

            if (status == TPCANStatus.PCAN_ERROR_OK)
            {
                int len = Math.Clamp((int)msg.LEN, 0, 8);
                byte[] data = new byte[len];
                Array.Copy(msg.DATA, data, len);
                bool extended = msg.MSGTYPE.HasFlag(TPCANMessageType.PCAN_MESSAGE_EXTENDED);
                var frame = new CanFrame(msg.ID, data, extended, CanFrameDirection.Rx);
                FrameReceived?.Invoke(this, new CanFrameReceivedEventArgs(frame));
                continue; // immediately try to drain more queued frames
            }

            if (status == TPCANStatus.PCAN_ERROR_QRCVEMPTY)
            {
                Thread.Sleep(1);
                continue;
            }

            Log($"CAN_Read status 0x{(uint)status:X}.", LogLevel.Warning);
            Thread.Sleep(20);
        }
    }

    private static (ushort channel, int bitrate) ParseConnectionString(string s)
    {
        string[] parts = s.Split(';', StringSplitOptions.TrimEntries);
        string channelName = parts.Length > 0 ? parts[0] : "USB1";
        int bitrate = parts.Length > 1 && int.TryParse(parts[1], out int br) ? br : 500000;

        ushort channel = channelName.ToUpperInvariant() switch
        {
            "USB1" => 0x51,
            "USB2" => 0x52,
            "USB3" => 0x53,
            "USB4" => 0x54,
            _ => 0x51
        };
        return (channel, bitrate);
    }

    private static TPCANBaudrate BitrateToEnum(int bitrate) => bitrate switch
    {
        1000000 => TPCANBaudrate.PCAN_BAUD_1M,
        800000 => TPCANBaudrate.PCAN_BAUD_800K,
        500000 => TPCANBaudrate.PCAN_BAUD_500K,
        250000 => TPCANBaudrate.PCAN_BAUD_250K,
        125000 => TPCANBaudrate.PCAN_BAUD_125K,
        100000 => TPCANBaudrate.PCAN_BAUD_100K,
        50000 => TPCANBaudrate.PCAN_BAUD_50K,
        20000 => TPCANBaudrate.PCAN_BAUD_20K,
        10000 => TPCANBaudrate.PCAN_BAUD_10K,
        _ => TPCANBaudrate.PCAN_BAUD_500K
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

    // ---- PCAN-Basic P/Invoke surface -------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct TPCANMsg
    {
        public uint ID;
        public TPCANMessageType MSGTYPE;
        public byte LEN;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] DATA;
    }

    [Flags]
    private enum TPCANMessageType : byte
    {
        PCAN_MESSAGE_STANDARD = 0x00,
        PCAN_MESSAGE_RTR = 0x01,
        PCAN_MESSAGE_EXTENDED = 0x02,
    }

    private enum TPCANStatus : uint
    {
        PCAN_ERROR_OK = 0x00000,
        PCAN_ERROR_QRCVEMPTY = 0x00020,
    }

    private enum TPCANBaudrate : ushort
    {
        PCAN_BAUD_1M = 0x0014,
        PCAN_BAUD_800K = 0x0016,
        PCAN_BAUD_500K = 0x001C,
        PCAN_BAUD_250K = 0x011C,
        PCAN_BAUD_125K = 0x031C,
        PCAN_BAUD_100K = 0x432F,
        PCAN_BAUD_50K = 0x472F,
        PCAN_BAUD_20K = 0x532F,
        PCAN_BAUD_10K = 0x672F,
    }

    [DllImport(DllName, EntryPoint = "CAN_Initialize")]
    private static extern TPCANStatus CAN_Initialize(ushort channel, TPCANBaudrate btr0Btr1, byte hwType, uint ioPort, ushort interrupt);

    [DllImport(DllName, EntryPoint = "CAN_Uninitialize")]
    private static extern TPCANStatus CAN_Uninitialize(ushort channel);

    [DllImport(DllName, EntryPoint = "CAN_Write")]
    private static extern TPCANStatus CAN_Write(ushort channel, ref TPCANMsg messageBuffer);

    [DllImport(DllName, EntryPoint = "CAN_Read")]
    private static extern TPCANStatus CAN_Read(ushort channel, out TPCANMsg messageBuffer, out ulong timestampBuffer);
}
