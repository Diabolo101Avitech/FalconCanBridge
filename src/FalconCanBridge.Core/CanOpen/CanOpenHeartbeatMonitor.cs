using System;
using System.Collections.Generic;
using System.Threading;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.Core.CanOpen;

public sealed class NodeStateChangedEventArgs : EventArgs
{
    public int NodeId { get; }
    public NmtState State { get; }
    public NmtState PreviousState { get; }

    public NodeStateChangedEventArgs(int nodeId, NmtState state, NmtState previousState)
    {
        NodeId = nodeId;
        State = state;
        PreviousState = previousState;
    }
}

public sealed class NodeTimedOutEventArgs : EventArgs
{
    public int NodeId { get; }
    public NodeTimedOutEventArgs(int nodeId) => NodeId = nodeId;
}

/// <summary>
/// Tracks CANopen node liveness/state from heartbeat frames (COB-ID 0x700+nodeId, CiA 301
/// §7.2.8.3.1) - the standard way a CANopen slave reports "I'm alive and here's my NMT state"
/// without the master having to poll it. Feed every received <see cref="CanFrame"/> to
/// <see cref="OnCanFrameReceived"/> (non-heartbeat frames are ignored, cheaply); a background
/// timer raises <see cref="NodeTimedOut"/> for any node that stops producing heartbeats within
/// <see cref="TimeoutMs"/> - configure this comfortably above the node's actual heartbeat
/// producer time (a common default for small CANopen stacks is a 1000 ms producer time, so this
/// class defaults its consumer timeout to 2000 ms).
///
/// Tracks every node ID it happens to see heartbeats from, not just one - harmless if your bus
/// only has a single CANopen node, and lets <see cref="GetState"/> be queried for any of them.
/// </summary>
public sealed class CanOpenHeartbeatMonitor : IDisposable
{
    private sealed class NodeInfo
    {
        public NmtState State = NmtState.Unknown;
        public DateTime LastSeenUtc;
    }

    private const uint HeartbeatBase = 0x700u;

    private readonly object _lock = new();
    private readonly Dictionary<int, NodeInfo> _nodes = new();
    private readonly Timer _timeoutTimer;

    public int TimeoutMs { get; }

    public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;
    public event EventHandler<NodeTimedOutEventArgs>? NodeTimedOut;

    public CanOpenHeartbeatMonitor(int timeoutMs = 2000)
    {
        TimeoutMs = timeoutMs;
        _timeoutTimer = new Timer(CheckTimeouts, null, 500, 500);
    }

    /// <summary>Feed every received CAN frame here; anything outside the heartbeat COB-ID range (0x701-0x77F) is ignored.</summary>
    public void OnCanFrameReceived(CanFrame frame)
    {
        // The predefined connection set only ever uses standard (11-bit) IDs - an extended (29-bit)
        // frame that happens to numerically fall in this range is unrelated bus traffic, not a
        // heartbeat, and must not be allowed to spoof one.
        if (frame.IsExtended) return;
        if (frame.Id <= HeartbeatBase || frame.Id > HeartbeatBase + CanOpenCobId.MaxNodeId) return;
        if (frame.Data.Length < 1) return;

        int nodeId = (int)(frame.Id - HeartbeatBase);
        // Mask off bit 7: it's a toggle bit used only by the legacy node-guarding protocol, not heartbeat.
        var newState = (NmtState)(frame.Data[0] & 0x7F);

        NodeStateChangedEventArgs? changedArgs = null;
        lock (_lock)
        {
            if (!_nodes.TryGetValue(nodeId, out var info))
            {
                info = new NodeInfo();
                _nodes[nodeId] = info;
            }

            var previous = info.State;
            info.State = newState;
            info.LastSeenUtc = DateTime.UtcNow;

            if (previous != newState)
            {
                changedArgs = new NodeStateChangedEventArgs(nodeId, newState, previous);
            }
        }

        if (changedArgs is not null)
        {
            NodeStateChanged?.Invoke(this, changedArgs);
        }
    }

    private void CheckTimeouts(object? state)
    {
        List<int>? timedOut = null;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _nodes)
            {
                if ((now - kvp.Value.LastSeenUtc).TotalMilliseconds > TimeoutMs)
                {
                    (timedOut ??= new List<int>()).Add(kvp.Key);
                }
            }

            if (timedOut is not null)
            {
                foreach (var id in timedOut) _nodes.Remove(id);
            }
        }

        if (timedOut is null) return;
        foreach (var id in timedOut)
        {
            NodeTimedOut?.Invoke(this, new NodeTimedOutEventArgs(id));
        }
    }

    /// <summary>Last-known NMT state for a node, or <see cref="NmtState.Unknown"/> if no heartbeat has been seen (or it has since timed out).</summary>
    public NmtState GetState(int nodeId)
    {
        lock (_lock)
        {
            return _nodes.TryGetValue(nodeId, out var info) ? info.State : NmtState.Unknown;
        }
    }

    public void Dispose() => _timeoutTimer.Dispose();
}
