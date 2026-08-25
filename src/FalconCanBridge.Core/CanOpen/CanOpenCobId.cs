using System;

namespace FalconCanBridge.Core.CanOpen;

/// <summary>Which CANopen predefined-connection-set function a COB-ID belongs to.</summary>
public enum CanOpenFunctionCode
{
    Nmt,
    Sync,
    Emcy,
    Tpdo1, Tpdo2, Tpdo3, Tpdo4,
    Rpdo1, Rpdo2, Rpdo3, Rpdo4,

    /// <summary>SDO server-&gt;client (i.e. node-&gt;master) direction - the node's "SDO transmit" (the SDO response).</summary>
    SdoTx,

    /// <summary>SDO client-&gt;server (i.e. master-&gt;node) direction - the node's "SDO receive" (the SDO request).</summary>
    SdoRx,

    Heartbeat,
    Unknown
}

/// <summary>
/// COB-ID arithmetic for CANopen's "predefined connection set" (CiA 301 §7.3.5) - the fixed
/// function-code-plus-node-ID scheme almost every simple CANopen slave (including a small
/// STM32-based panel node) uses instead of dynamically remapped COB-IDs. Node IDs are 1-127;
/// 0 is reserved for NMT broadcast only.
///
/// A PDO COB-ID computed here is directly usable as a <see cref="Models.SignalMapping"/>'s
/// CanId - PDOs are nothing more than plain CAN frames at these fixed IDs, so the existing
/// byte/bit mapping engine (<see cref="Mapping.MappingEngine"/>) already "speaks" CANopen PDOs
/// without any changes of its own; this class exists so you don't have to hand-compute the hex
/// IDs, and so <see cref="TryDecode"/> can label the CAN Traffic view.
/// </summary>
public static class CanOpenCobId
{
    public const uint Nmt = 0x000;
    public const uint Sync = 0x080;

    public const int MinNodeId = 1;
    public const int MaxNodeId = 127;

    public static uint Emcy(int nodeId) => 0x080u + CheckNodeId(nodeId);

    public static uint Heartbeat(int nodeId) => 0x700u + CheckNodeId(nodeId);

    /// <summary>Node's SDO transmit COB-ID (node -&gt; master, i.e. the SDO *response*).</summary>
    public static uint SdoTx(int nodeId) => 0x580u + CheckNodeId(nodeId);

    /// <summary>Node's SDO receive COB-ID (master -&gt; node, i.e. the SDO *request*).</summary>
    public static uint SdoRx(int nodeId) => 0x600u + CheckNodeId(nodeId);

    /// <summary>Transmit-PDO (device -&gt; master) COB-ID for PDO number 1-4, default predefined-connection-set ID.</summary>
    public static uint Tpdo(int pdoNumber, int nodeId) => TpdoBase(pdoNumber) + CheckNodeId(nodeId);

    /// <summary>Receive-PDO (master -&gt; device) COB-ID for PDO number 1-4, default predefined-connection-set ID.</summary>
    public static uint Rpdo(int pdoNumber, int nodeId) => RpdoBase(pdoNumber) + CheckNodeId(nodeId);

    private static uint TpdoBase(int pdoNumber) => pdoNumber switch
    {
        1 => 0x180u,
        2 => 0x280u,
        3 => 0x380u,
        4 => 0x480u,
        _ => throw new ArgumentOutOfRangeException(nameof(pdoNumber), "PDO number must be 1-4.")
    };

    private static uint RpdoBase(int pdoNumber) => pdoNumber switch
    {
        1 => 0x200u,
        2 => 0x300u,
        3 => 0x400u,
        4 => 0x500u,
        _ => throw new ArgumentOutOfRangeException(nameof(pdoNumber), "PDO number must be 1-4.")
    };

    private static uint CheckNodeId(int nodeId)
    {
        if (nodeId < MinNodeId || nodeId > MaxNodeId)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), $"CANopen node ID must be {MinNodeId}-{MaxNodeId}.");
        }
        return (uint)nodeId;
    }

    /// <summary>
    /// (RangeBase, Function) pairs for every 0x80-wide block of the predefined connection set,
    /// used by <see cref="TryDecode"/>. NMT (0x000, no node ID) and SYNC (0x080 exactly) are
    /// handled separately since they don't carry a node ID at all.
    /// </summary>
    private static readonly (uint RangeBase, CanOpenFunctionCode Fn)[] NodeRanges =
    {
        (0x080u, CanOpenFunctionCode.Emcy),
        (0x180u, CanOpenFunctionCode.Tpdo1),
        (0x200u, CanOpenFunctionCode.Rpdo1),
        (0x280u, CanOpenFunctionCode.Tpdo2),
        (0x300u, CanOpenFunctionCode.Rpdo2),
        (0x380u, CanOpenFunctionCode.Tpdo3),
        (0x400u, CanOpenFunctionCode.Rpdo3),
        (0x480u, CanOpenFunctionCode.Tpdo4),
        (0x500u, CanOpenFunctionCode.Rpdo4),
        (0x580u, CanOpenFunctionCode.SdoTx),
        (0x600u, CanOpenFunctionCode.SdoRx),
        (0x700u, CanOpenFunctionCode.Heartbeat)
    };

    /// <summary>
    /// Best-effort classification of a raw standard-frame COB-ID against the predefined connection
    /// set, for labeling the CAN Traffic view. Not authoritative if a node remaps its PDO COB-IDs
    /// via SDO (out of scope here - see README "CANopen support" limitations).
    /// </summary>
    public static bool TryDecode(uint cobId, out CanOpenFunctionCode function, out int nodeId)
    {
        function = CanOpenFunctionCode.Unknown;
        nodeId = 0;

        if (cobId == Nmt) { function = CanOpenFunctionCode.Nmt; return true; }
        if (cobId == Sync) { function = CanOpenFunctionCode.Sync; return true; }

        foreach (var (rangeBase, fn) in NodeRanges)
        {
            if (cobId < rangeBase) continue;
            uint offset = cobId - rangeBase;
            if (offset >= MinNodeId && offset <= MaxNodeId)
            {
                function = fn;
                nodeId = (int)offset;
                return true;
            }
        }

        return false;
    }
}
