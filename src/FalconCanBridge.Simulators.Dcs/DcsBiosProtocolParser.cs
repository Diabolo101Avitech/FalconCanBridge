using System;

namespace FalconCanBridge.Simulators.Dcs;

public readonly struct DcsBiosUpdate
{
    /// <summary>Byte offset into the DCS-BIOS export memory image.</summary>
    public ushort Address { get; }
    public byte[] Data { get; }

    public DcsBiosUpdate(ushort address, byte[] data)
    {
        Address = address;
        Data = data;
    }
}

/// <summary>
/// Stateful parser for the DCS-BIOS export stream protocol.
///
/// The stream is a continuous sequence of bytes (DCS-BIOS's Lua export hook writes it to a
/// UDP socket once per sim frame, so a single logical "frame" of updates may be split across
/// several UDP datagrams, or several frames may arrive in a single datagram - the parser is
/// therefore fed raw bytes via <see cref="Feed"/> and keeps its state between calls rather
/// than assuming any alignment with datagram boundaries).
///
/// Format: a run of four 0x55 sync bytes marks a frame boundary. Following the sync, the
/// stream contains zero or more (address: UInt16 LE, count: UInt16 LE, data: count bytes)
/// records, each describing a byte-range write into DCS-BIOS's conceptual export memory image
/// (the same layout the per-aircraft "control reference" JSON files address controls by).
/// </summary>
/// <remarks>
/// Note on sync-run false positives: because the sync marker is just "four 0x55 bytes in a row"
/// with no outer length prefix, a run of 1-3 coincidental 0x55 bytes inside a genuine
/// address/count/data record (rare, but possible for any given byte) can momentarily desync the
/// parser until the next real 4x0x55 frame boundary. This is an accepted characteristic of the
/// DCS-BIOS wire format itself (every reference parser has the same behavior), not something
/// fixable purely on the receiving end - any resulting garbage self-corrects within one exported
/// frame (DCS exports at the sim frame rate), so it's a non-issue for gauge-type telemetry.
/// </remarks>
public sealed class DcsBiosProtocolParser
{
    // Note: there's no separate "AddressLow" state - StartAddressWithByte() consumes the low
    // byte immediately (it's whatever byte fell through from SyncByte0/SyncByte1To3) and jumps
    // straight to AddressHigh for the next byte.
    private enum State { SyncByte0, SyncByte1To3, AddressHigh, CountLow, CountHigh, Data }

    private State _state = State.SyncByte0;
    private int _syncRun;
    private ushort _address;
    private ushort _count;
    private byte[] _dataBuffer = Array.Empty<byte>();
    private int _dataIndex;

    /// <summary>Raised once for every (address, data) write parsed out of the stream.</summary>
    public event Action<DcsBiosUpdate>? UpdateParsed;

    /// <summary>Raised each time a 4x 0x55 sync run is seen, i.e. once per exported sim frame.</summary>
    public event Action? FrameSync;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            FeedByte(b);
        }
    }

    private void FeedByte(byte b)
    {
        switch (_state)
        {
            case State.SyncByte0:
                if (b == 0x55) { _syncRun = 1; _state = State.SyncByte1To3; }
                else { StartAddressWithByte(b); }
                break;

            case State.SyncByte1To3:
                if (b == 0x55)
                {
                    _syncRun++;
                    if (_syncRun == 4)
                    {
                        FrameSync?.Invoke();
                        _state = State.SyncByte0;
                        _syncRun = 0;
                    }
                }
                else
                {
                    // Not actually a full sync run - treat this byte as the low byte of a new address field.
                    StartAddressWithByte(b);
                }
                break;

            case State.AddressHigh:
                _address |= (ushort)(b << 8);
                _state = State.CountLow;
                break;

            case State.CountLow:
                _count = b;
                _state = State.CountHigh;
                break;

            case State.CountHigh:
                _count |= (ushort)(b << 8);
                if (_count == 0)
                {
                    // Zero-length record: nothing to read, go straight back to looking for sync/next record.
                    _state = State.SyncByte0;
                }
                else
                {
                    _dataBuffer = new byte[_count];
                    _dataIndex = 0;
                    _state = State.Data;
                }
                break;

            case State.Data:
                _dataBuffer[_dataIndex++] = b;
                if (_dataIndex >= _count)
                {
                    UpdateParsed?.Invoke(new DcsBiosUpdate(_address, _dataBuffer));
                    _state = State.SyncByte0;
                }
                break;
        }
    }

    private void StartAddressWithByte(byte b)
    {
        _address = b;
        _state = State.AddressHigh;
    }
}
