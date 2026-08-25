namespace FalconCanBridge.Core.CanOpen;

/// <summary>
/// NMT (Network Management) commands a CANopen master can send to a node - CiA 301 §7.2.8.3.1.
/// Sent as a 2-byte frame on COB-ID 0x000 (predefined connection set): byte 0 = command,
/// byte 1 = target node ID (0 = broadcast to every node on the bus).
/// </summary>
public enum NmtCommand : byte
{
    /// <summary>Enter NMT state Operational - PDOs are only exchanged once a node is in this state.</summary>
    Start = 0x01,

    /// <summary>Enter NMT state Stopped - only NMT and heartbeat/node-guarding are processed.</summary>
    Stop = 0x02,

    /// <summary>Enter NMT state Pre-operational - SDO works, PDOs do not.</summary>
    EnterPreOperational = 0x80,

    /// <summary>Reset the application layer (like a power-on reset of the device firmware's application).</summary>
    ResetNode = 0x81,

    /// <summary>Reset only the communication layer (device re-applies its communication-related object dictionary entries).</summary>
    ResetCommunication = 0x82
}

/// <summary>
/// NMT states as reported in a node's heartbeat byte (CiA 301 §7.2.8.3.1, Table 98). The top bit
/// of that byte is a toggle used only by the legacy node-guarding protocol, not heartbeat -
/// <see cref="CanOpenHeartbeatMonitor"/> masks it off before comparing against this enum.
/// </summary>
public enum NmtState : byte
{
    /// <summary>Local-only placeholder meaning "no heartbeat seen yet (or it has since timed out)" - never actually sent on the bus.</summary>
    Unknown = 0xFF,

    /// <summary>Sent exactly once, right after the node powers up/resets, before it settles into Pre-operational.</summary>
    BootUp = 0x00,

    Stopped = 0x04,
    Operational = 0x05,
    PreOperational = 0x7F
}
