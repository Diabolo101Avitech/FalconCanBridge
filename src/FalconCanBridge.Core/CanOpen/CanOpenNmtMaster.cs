using System;
using System.Threading;
using System.Threading.Tasks;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.CanOpen;

/// <summary>
/// Sends NMT (Network Management) commands to one or all CANopen nodes on the bus - the
/// master-side half of CiA 301's NMT service. Stateless (it doesn't track node state itself; see
/// <see cref="CanOpenHeartbeatMonitor"/> for that) - it just formats and transmits the 2-byte
/// command frame on COB-ID 0x000 through whichever <see cref="ICanBusAdapter"/> is currently open
/// (SLCAN or PCAN - CANopen is a payload-level protocol, indifferent to the physical transport).
/// </summary>
public sealed class CanOpenNmtMaster
{
    private readonly ICanBusAdapter _adapter;

    public CanOpenNmtMaster(ICanBusAdapter adapter) => _adapter = adapter;

    /// <summary>Sends an NMT command. nodeId 0 broadcasts to every node on the bus (CiA 301 default).</summary>
    public Task SendAsync(NmtCommand command, int nodeId = 0, CancellationToken cancellationToken = default)
    {
        if (nodeId < 0 || nodeId > CanOpenCobId.MaxNodeId)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), $"NMT target node ID must be 0 (broadcast) to {CanOpenCobId.MaxNodeId}.");
        }

        byte[] data = { (byte)command, (byte)nodeId };
        var frame = new CanFrame(CanOpenCobId.Nmt, data, false, CanFrameDirection.Tx);
        return _adapter.SendAsync(frame, cancellationToken);
    }

    public Task StartNodeAsync(int nodeId = 0, CancellationToken cancellationToken = default)
        => SendAsync(NmtCommand.Start, nodeId, cancellationToken);

    public Task StopNodeAsync(int nodeId = 0, CancellationToken cancellationToken = default)
        => SendAsync(NmtCommand.Stop, nodeId, cancellationToken);

    public Task EnterPreOperationalAsync(int nodeId = 0, CancellationToken cancellationToken = default)
        => SendAsync(NmtCommand.EnterPreOperational, nodeId, cancellationToken);

    public Task ResetNodeAsync(int nodeId = 0, CancellationToken cancellationToken = default)
        => SendAsync(NmtCommand.ResetNode, nodeId, cancellationToken);

    public Task ResetCommunicationAsync(int nodeId = 0, CancellationToken cancellationToken = default)
        => SendAsync(NmtCommand.ResetCommunication, nodeId, cancellationToken);
}
