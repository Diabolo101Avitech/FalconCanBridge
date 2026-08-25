using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.CanOpen;

/// <summary>Thrown when a CANopen SDO transfer is aborted by the server, or the response is malformed/unexpected.</summary>
public sealed class CanOpenSdoException : Exception
{
    /// <summary>The 32-bit SDO abort code (CiA 301 Table 22), 0 if this exception wasn't raised by an abort frame.</summary>
    public uint AbortCode { get; }

    public CanOpenSdoException(string message, uint abortCode = 0) : base(message) => AbortCode = abortCode;
}

/// <summary>
/// Minimal CANopen SDO (Service Data Object) *client* - the master-side half of CiA 301's SDO
/// service, used to read/write a single object-dictionary entry on one node (e.g. a configuration
/// parameter that isn't exchanged cyclically via PDO). Only the "expedited" transfer variant is
/// implemented (values up to 4 bytes in a single request/response pair) since that covers the
/// overwhelming majority of simple I/O/configuration registers on a small STM32 panel node -
/// segmented transfers (for values &gt;4 bytes, e.g. a firmware version string) are NOT supported;
/// see README "CANopen support" limitations.
///
/// One instance targets one node ID and serializes its own transfers (SDO is a strict
/// request/response protocol - a node only has one SDO "channel" active at a time), but does not
/// interfere with PDOs, NMT, or another node's SDO traffic on the same bus.
/// </summary>
public sealed class CanOpenSdoClient : IDisposable
{
    private readonly ICanBusAdapter _adapter;
    private readonly int _nodeId;
    private readonly int _timeoutMs;
    private readonly SemaphoreSlim _transferLock = new(1, 1);

    private TaskCompletionSource<byte[]>? _pendingResponse;

    public CanOpenSdoClient(ICanBusAdapter adapter, int nodeId, int timeoutMs = 1000)
    {
        _adapter = adapter;
        _nodeId = nodeId;
        _timeoutMs = timeoutMs;
        _adapter.FrameReceived += OnFrameReceived;
    }

    private void OnFrameReceived(object? sender, CanFrameReceivedEventArgs e)
    {
        // Standard (11-bit) IDs only - see the matching guard in CanOpenHeartbeatMonitor for why.
        if (e.Frame.IsExtended) return;
        if (e.Frame.Id != CanOpenCobId.SdoTx(_nodeId)) return;
        _pendingResponse?.TrySetResult(e.Frame.Data);
    }

    /// <summary>Reads 1-4 raw bytes from the object dictionary entry at index:subIndex (expedited SDO upload).</summary>
    public async Task<byte[]> UploadAsync(ushort index, byte subIndex, CancellationToken cancellationToken = default)
    {
        await _transferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResponse = tcs;

            byte[] request = new byte[8];
            request[0] = 0x40; // initiate upload request (ccs=2)
            request[1] = (byte)(index & 0xFF);
            request[2] = (byte)(index >> 8);
            request[3] = subIndex;

            await _adapter.SendAsync(new CanFrame(CanOpenCobId.SdoRx(_nodeId), request, false, CanFrameDirection.Tx), cancellationToken).ConfigureAwait(false);

            byte[] response;
            try
            {
                response = await WaitForResponseAsync(tcs.Task, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await TryAbortAsync(index, subIndex).ConfigureAwait(false);
                throw;
            }

            return ParseUploadResponse(response, index, subIndex);
        }
        finally
        {
            _pendingResponse = null;
            _transferLock.Release();
        }
    }

    /// <summary>Writes 1-4 raw bytes to the object dictionary entry at index:subIndex (expedited SDO download).</summary>
    public async Task DownloadAsync(ushort index, byte subIndex, byte[] value, CancellationToken cancellationToken = default)
    {
        if (value is null || value.Length is < 1 or > 4)
        {
            throw new ArgumentException("Expedited SDO download supports 1-4 data bytes only.", nameof(value));
        }

        await _transferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResponse = tcs;

            int unused = 4 - value.Length;
            byte commandSpecifier = (byte)(0x20 | (unused << 2) | 0x02 | 0x01); // ccs=1 (download), e=1 (expedited), s=1 (size indicated)

            byte[] request = new byte[8];
            request[0] = commandSpecifier;
            request[1] = (byte)(index & 0xFF);
            request[2] = (byte)(index >> 8);
            request[3] = subIndex;
            Array.Copy(value, 0, request, 4, value.Length);

            await _adapter.SendAsync(new CanFrame(CanOpenCobId.SdoRx(_nodeId), request, false, CanFrameDirection.Tx), cancellationToken).ConfigureAwait(false);

            byte[] response;
            try
            {
                response = await WaitForResponseAsync(tcs.Task, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await TryAbortAsync(index, subIndex).ConfigureAwait(false);
                throw;
            }

            ParseDownloadResponse(response, index, subIndex);
        }
        finally
        {
            _pendingResponse = null;
            _transferLock.Release();
        }
    }

    // ---- Typed convenience helpers (CANopen expedited SDO values are always little-endian) --------

    public async Task<byte> UploadUInt8Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => (await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false))[0];

    public async Task<sbyte> UploadInt8Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => unchecked((sbyte)(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false))[0]);

    public async Task<ushort> UploadUInt16Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => BitConverter.ToUInt16(PadLittleEndian(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false), 2), 0);

    public async Task<short> UploadInt16Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => BitConverter.ToInt16(PadLittleEndian(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false), 2), 0);

    public async Task<uint> UploadUInt32Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => BitConverter.ToUInt32(PadLittleEndian(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false), 4), 0);

    public async Task<int> UploadInt32Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => BitConverter.ToInt32(PadLittleEndian(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false), 4), 0);

    public async Task<float> UploadFloat32Async(ushort index, byte subIndex, CancellationToken cancellationToken = default)
        => BitConverter.ToSingle(PadLittleEndian(await UploadAsync(index, subIndex, cancellationToken).ConfigureAwait(false), 4), 0);

    public Task DownloadUInt8Async(ushort index, byte subIndex, byte value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, new[] { value }, cancellationToken);

    public Task DownloadInt8Async(ushort index, byte subIndex, sbyte value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, new[] { unchecked((byte)value) }, cancellationToken);

    public Task DownloadUInt16Async(ushort index, byte subIndex, ushort value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, BitConverter.GetBytes(value), cancellationToken);

    public Task DownloadInt16Async(ushort index, byte subIndex, short value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, BitConverter.GetBytes(value), cancellationToken);

    public Task DownloadUInt32Async(ushort index, byte subIndex, uint value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, BitConverter.GetBytes(value), cancellationToken);

    public Task DownloadInt32Async(ushort index, byte subIndex, int value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, BitConverter.GetBytes(value), cancellationToken);

    public Task DownloadFloat32Async(ushort index, byte subIndex, float value, CancellationToken cancellationToken = default)
        => DownloadAsync(index, subIndex, BitConverter.GetBytes(value), cancellationToken);

    // ---- Internals ------------------------------------------------------------------------------

    /// <summary>
    /// Best-effort client-initiated abort sent after a transfer times out, so a compliant SDO
    /// server drops its half-finished transaction instead of possibly replying later - without
    /// this, a late reply to an abandoned request could otherwise be misread as the response to
    /// a *subsequent* transfer for the same index:subIndex (CANopen's SDO protocol carries no
    /// transaction/sequence number of its own to tell them apart). This narrows that window but
    /// doesn't eliminate it outright: only rely on back-to-back SDO reads/writes of the same
    /// object dictionary entry being trustworthy when transfers aren't routinely timing out.
    /// </summary>
    private async Task TryAbortAsync(ushort index, byte subIndex)
    {
        try
        {
            byte[] abort = new byte[8];
            abort[0] = 0x80;
            abort[1] = (byte)(index & 0xFF);
            abort[2] = (byte)(index >> 8);
            abort[3] = subIndex;
            BitConverter.GetBytes(0x05040000u).CopyTo(abort, 4); // CiA 301 Table 22: "SDO protocol timed out"

            await _adapter.SendAsync(new CanFrame(CanOpenCobId.SdoRx(_nodeId), abort, false, CanFrameDirection.Tx)).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort - if even the abort can't be sent (e.g. the adapter just closed), there's
            // nothing more to do here; the caller already has the original TimeoutException.
        }
    }

    private async Task<byte[]> WaitForResponseAsync(Task<byte[]> responseTask, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var delayTask = Task.Delay(Timeout.Infinite, linkedCts.Token);

        var completed = await Task.WhenAny(responseTask, delayTask).ConfigureAwait(false);
        if (completed != responseTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"SDO transfer to node {_nodeId} timed out after {_timeoutMs} ms (no response on COB-ID 0x{CanOpenCobId.SdoTx(_nodeId):X}).");
        }

        return await responseTask.ConfigureAwait(false);
    }

    private byte[] ParseUploadResponse(byte[] data, ushort expectedIndex, byte expectedSubIndex)
    {
        if (data.Length < 4)
        {
            throw new CanOpenSdoException("SDO upload response frame was shorter than 4 bytes.");
        }

        byte commandSpecifier = data[0];
        if (commandSpecifier == 0x80)
        {
            throw new CanOpenSdoException(
                $"SDO upload of 0x{expectedIndex:X4}:{expectedSubIndex} was aborted by node {_nodeId}.", ReadAbortCode(data));
        }

        CheckIndexMatch(data, expectedIndex, expectedSubIndex, "upload");

        if ((commandSpecifier & 0xE0) != 0x40)
        {
            throw new CanOpenSdoException($"Unexpected SDO upload response command specifier 0x{commandSpecifier:X2}.");
        }

        bool expedited = (commandSpecifier & 0x02) != 0;
        if (!expedited)
        {
            throw new CanOpenSdoException("Segmented SDO transfers are not supported by this client - the node reported a non-expedited upload.");
        }

        bool sizeIndicated = (commandSpecifier & 0x01) != 0;
        int length = sizeIndicated ? 4 - ((commandSpecifier >> 2) & 0x03) : 4;
        length = Math.Clamp(length, 1, 4);

        byte[] result = new byte[length];
        Array.Copy(data, 4, result, 0, Math.Min(length, data.Length - 4));
        return result;
    }

    private void ParseDownloadResponse(byte[] data, ushort expectedIndex, byte expectedSubIndex)
    {
        if (data.Length < 4)
        {
            throw new CanOpenSdoException("SDO download response frame was shorter than 4 bytes.");
        }

        byte commandSpecifier = data[0];
        if (commandSpecifier == 0x80)
        {
            throw new CanOpenSdoException(
                $"SDO download of 0x{expectedIndex:X4}:{expectedSubIndex} was aborted by node {_nodeId}.", ReadAbortCode(data));
        }

        if (commandSpecifier != 0x60)
        {
            throw new CanOpenSdoException($"Unexpected SDO download confirmation command specifier 0x{commandSpecifier:X2} (expected 0x60).");
        }

        CheckIndexMatch(data, expectedIndex, expectedSubIndex, "download");
    }

    private static void CheckIndexMatch(byte[] data, ushort expectedIndex, byte expectedSubIndex, string operationName)
    {
        ushort responseIndex = (ushort)(data[1] | (data[2] << 8));
        byte responseSubIndex = data[3];
        if (responseIndex != expectedIndex || responseSubIndex != expectedSubIndex)
        {
            throw new CanOpenSdoException(
                $"SDO {operationName} response was for 0x{responseIndex.ToString("X4", CultureInfo.InvariantCulture)}:{responseSubIndex}, " +
                $"expected 0x{expectedIndex.ToString("X4", CultureInfo.InvariantCulture)}:{expectedSubIndex}.");
        }
    }

    private static uint ReadAbortCode(byte[] data) => data.Length >= 8 ? BitConverter.ToUInt32(data, 4) : 0;

    /// <summary>Pads a short expedited-SDO payload out to <paramref name="width"/> bytes (zero-extended) so BitConverter can read it as a fixed-width little-endian value.</summary>
    private static byte[] PadLittleEndian(byte[] bytes, int width)
    {
        if (bytes.Length == width) return bytes;
        byte[] padded = new byte[width];
        Array.Copy(bytes, padded, Math.Min(bytes.Length, width));
        return padded;
    }

    public void Dispose() => _adapter.FrameReceived -= OnFrameReceived;
}
