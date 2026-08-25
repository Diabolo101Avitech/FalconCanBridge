# Falcon CAN Bridge

A Windows C# / WPF application that reads live telemetry out of **Falcon 4 BMS** and **DCS
World** (via DCS-BIOS), and both directions of a **CAN bus** talking to your own **STM32-based**
cockpit-panel hardware: sim data drives gauges/lights on the CAN bus, and switches/buttons on the
CAN bus drive commands back into the sim.

```
 Falcon 4 BMS  ---shared memory--->  Falcon4Connector  --\
                                                            >-- MappingEngine --> CAN frames --> STM32 panel bus
 DCS World     ---DCS-BIOS/UDP--->  DcsBiosConnector  --/                    <-- CAN frames <--/
                       ^                                                              |
                       \--------------------- keystrokes / DCS-BIOS commands <--------/
```

## Solution layout

| Project | Responsibility |
|---|---|
| `FalconCanBridge.Core` | Shared models (`CanFrame`, `TelemetrySnapshot`, `SignalMapping`), the `ISimulatorConnector`/`ICanBusAdapter` interfaces, `MappingEngine` (the bidirectional signal <-> CAN-frame packing/unpacking engine), and an optional `CanOpen/` layer (NMT master, heartbeat consumer, expedited SDO client) for STM32 nodes that speak CANopen instead of raw custom frames - see "CANopen support" below. |
| `FalconCanBridge.Simulators.Falcon4` | Reads the `FalconSharedMemoryArea` shared-memory block BMS publishes; sends input back as simulated keystrokes. |
| `FalconCanBridge.Simulators.Dcs` | DCS-BIOS UDP export-stream parser + command sender. |
| `FalconCanBridge.CanBus` | `SlcanSerialAdapter` (STM32 USB-CDC, SLCAN/LAWICEL protocol) and `PcanBasicAdapter` (PEAK PCAN-USB, optional). |
| `FalconCanBridge.App` | WPF UI: connection panel, live telemetry viewer, CAN mapping editor, CAN traffic monitor, log console. |

## Building

Requires the **.NET 8 SDK** and Windows (WPF, the BMS shared-memory reader, keystroke injection,
and `System.IO.Ports` are all Windows-only). This was authored and reviewed in an environment
without Windows/`dotnet` available to compile against, so **build it once locally before relying
on it** - see "Known limitations" below.

```
dotnet build FalconCanBridge.sln -c Release
dotnet run --project src/FalconCanBridge.App
```

Or open `FalconCanBridge.sln` in Visual Studio 2022+ and run `FalconCanBridge.App`.

## Using it

1. **Connections tab**: pick Falcon 4 BMS or DCS World, click Connect. Pick the CAN adapter type
   (SLCAN for a custom STM32 USB-CDC board, or PCAN-Basic for a PEAK dongle), fill in the COM
   port/bitrate (or PCAN channel), click Open.
2. **CAN Mapping tab**: load `config/mapping.sample.json` (loaded automatically on startup if
   present next to the exe) or build your own profile. Each row maps one signal to a byte/bit
   location inside a CAN frame, in one of two directions:
   - **SimToCan**: a named simulator telemetry signal drives bytes in an outgoing CAN frame
     (gauge needles, LEDs, 7-segment displays on your panel).
   - **CanToSim**: a byte/bit location inside an *incoming* CAN frame (a switch/button/encoder on
     your panel) is decoded and sent into the sim as a command.
   Several rows can share the same `CanId` to pack multiple signals into one 8-byte frame.
3. **Live Telemetry tab**: sanity-check the raw signal names/values coming from the connected
   simulator.
4. **CAN Traffic tab**: watch actual TX/RX frames on the bus - the fastest way to verify your
   mapping and your STM32 firmware agree on IDs/byte layout. A "CANopen" column best-effort-labels
   any frame whose ID matches the CANopen predefined connection set (e.g. "Heartbeat node 5"),
   whether or not you've ticked "Enable CANopen" below - it's just an ID-pattern label.
5. **Log tab**: connection/adapter status and errors.

## CANopen support (optional layer over the CAN adapter)

If your STM32 firmware speaks **CANopen** (CiA 301) instead of talking raw custom CAN frames,
tick **Enable CANopen** in the Connections tab (under the CAN adapter, once it's open) instead of
writing a bespoke frame format. CANopen is a payload-level protocol - it doesn't care whether the
bytes travel over the `SlcanSerialAdapter` or `PcanBasicAdapter` transport underneath, so this
works with either.

What's implemented (`FalconCanBridge.Core/CanOpen/`):

- **NMT master** (`CanOpenNmtMaster`): Start/Stop/Reset buttons in the UI, plus an "auto-start on
  Open" option that sends NMT Start to your configured Node ID as soon as the CAN adapter opens -
  a CANopen node only exchanges PDOs once it's in the Operational state, so without this (or your
  firmware self-transitioning to Operational) your PDOs will silently go nowhere.
- **Heartbeat consumer** (`CanOpenHeartbeatMonitor`): listens for the node's heartbeat (COB-ID
  `0x700+NodeID`) and shows its live NMT state (BootUp/Stopped/Operational/PreOperational) with a
  green/red status dot, and flags a heartbeat timeout (default 2000 ms) if the node goes quiet -
  requires your firmware to actually produce heartbeats (a fixed 1000 ms is a common default).
- **Expedited SDO client** (`CanOpenSdoClient`): a Read/Write test panel for one object dictionary
  index:subindex at a time (1-4 byte values - UInt8/Int8/UInt16/Int16/UInt32/Int32/Float32), for
  configuration registers that aren't exchanged cyclically (e.g. a calibration constant or a
  threshold), plus the same typed methods programmatically if you want to script something beyond
  the UI's one-shot Read/Write buttons.
- **PDOs need no dedicated UI at all**: a PDO is nothing more than a plain CAN frame at a fixed
  COB-ID, and the existing CAN Mapping tab already packs/unpacks arbitrary CAN IDs byte-for-byte -
  just set a mapping row's `CAN ID` to the PDO's COB-ID directly. `CanOpenCobId` documents (and can
  compute) the predefined connection set so you don't have to hand-add hex:

  | Function | COB-ID | Direction |
  |---|---|---|
  | NMT | `0x000` | master -> all |
  | SYNC | `0x080` | master -> all |
  | EMCY | `0x080 + NodeID` | node -> master |
  | TPDO1 / RPDO1 | `0x180 + NodeID` / `0x200 + NodeID` | node->master / master->node |
  | TPDO2 / RPDO2 | `0x280 + NodeID` / `0x300 + NodeID` | node->master / master->node |
  | TPDO3 / RPDO3 | `0x380 + NodeID` / `0x400 + NodeID` | node->master / master->node |
  | TPDO4 / RPDO4 | `0x480 + NodeID` / `0x500 + NodeID` | node->master / master->node |
  | SDO tx (response) / rx (request) | `0x580 + NodeID` / `0x600 + NodeID` | node->master / master->node |
  | Heartbeat | `0x700 + NodeID` | node -> master |

  e.g. node ID 5's TPDO1 is `0x185` - enter that as the mapping row's `CAN ID` exactly like any
  other frame, with `Direction=CanToSim` if the node is *sending* it (a switch/encoder value) or
  `Direction=SimToCan` if the PC is *sending* it as an RPDO (a gauge/LED command).

What's **not** implemented - keep these in mind when writing your STM32 firmware:

- **Segmented SDO transfers** (values >4 bytes, e.g. reading back a firmware-version string) -
  `CanOpenSdoClient` only speaks the expedited variant. Keep configuration objects to 4 bytes or
  fewer, or extend `CanOpenSdoClient` (it's a small, self-contained class) if you need more.
- **Dynamic PDO remapping via SDO** (changing which object dictionary entries a PDO carries at
  runtime) - this app always assumes the *default* predefined-connection-set COB-IDs above and
  whatever fixed byte layout you configure in the CAN Mapping tab; if your firmware remaps PDOs to
  different COB-IDs, update the mapping rows' `CAN ID` to match, but there's no SDO-driven
  auto-negotiation.
- **LSS** (automatic node-ID/bitrate assignment) - your STM32 firmware needs a fixed, known node ID
  configured some other way (DIP switches, a compiled-in constant, ...).
- **SYNC-driven synchronous PDOs** - this app never sends a SYNC frame itself and doesn't need to:
  PDOs here are always the simpler, more common "asynchronous"/event-driven kind, already
  rate-limited by each mapping row's `Rate ms`/`Threshold` settings exactly like non-CANopen
  mappings. If your firmware's PDOs are configured to only transmit on SYNC, either reconfigure
  them to transmit asynchronously, or add a small SYNC producer yourself (`CanOpenCobId.Sync` gives
  you the COB-ID; sending it is a one-line `ICanBusAdapter.SendAsync`).
- Like everything else in this repo, **this was written and reviewed without a live CANopen node
  to test against** (no Windows/dotnet/CAN hardware in the authoring environment) - the COB-ID
  arithmetic and SDO expedited-transfer command-specifier bytes follow CiA 301 exactly, but
  exercise the Start/Stop/Reset buttons and a couple of SDO reads against your actual node before
  relying on it, the same way you'd sanity-check the Falcon 4 field table against the Live
  Telemetry tab.

## Known limitations - please read before wiring real hardware

This was built and reviewed without access to a live BMS/DCS/STM32 rig to test end-to-end, so
some pieces are deliberately **configuration, not hardcoded assumptions**, and are marked as
best-effort:

- **Falcon 4 BMS field offsets**: `Falcon4FieldMap`'s built-in default table (loaded automatically,
  no JSON file required) now covers essentially every field BMS publishes across both its primary
  ("FalconSharedMemoryArea") and secondary ("FalconSharedMemoryArea2") shared-memory blocks -
  every scalar telemetry value, all 40 RWR contact slots, and every individual named
  light/HSI/altimeter/power/ECM status bit - several hundred signals in total. Rather than
  hand-typing byte offsets (which is exactly how this table went stale/wrong before - several of
  the originally-shipped fields, like `AltitudeMslFt`, didn't correspond to any real field in BMS's
  struct at all), the offsets are computed by the .NET marshaler itself
  (`Marshal.OffsetOf<T>`) against byte-for-byte vendored copies of lightningstools/F4SharedMem's
  own `BMS4FlightData`/`FlightData2` struct definitions (`src/FalconCanBridge.Simulators.Falcon4/Native/`,
  built from `src/lightningstools/F4SharedMem/Headers/*.cs` in this repo) - see the remarks on
  `BMS4FlightDataNative` for why that's correct by construction rather than a guess. This still
  wasn't run against a live BMS instance (no Windows/BMS available in the environment this was
  authored in), so **use the Live Telemetry tab to sanity-check a few known values** (altitude ~0
  on the runway, master caution light matching the cockpit, RWR contacts matching what's on the
  scope) before trusting them on real hardware - and if your BMS build reports a struct version
  newer than what's vendored here, some of the newest fields may be missing (harmless - see the
  version comments in `FlightData2.cs`) rather than wrong.
  An optional `config/falcon4-fields.sample.json` next to the exe still lets you ADD your own
  signals or OVERRIDE individual entries (friendlier alias names, tighter Min/Max, notes, ...) by
  `Name` on top of the built-in table, without recompiling - see `Falcon4FieldMap.MergeFromFile`.
  Not yet covered (separate shared-memory sections, out of scope for this pass): DED/PFL line text
  is decoded as free-text signals (`DEDLines_0..4`, `PFLLines_0..4`, ...), but IntelliVibe
  (haptic/event data), OSB button labels, radio client status, and the String/Drawing (HUD/MFD
  render) areas are not wired up - each would need its own reader against its own named
  shared-memory section, following the same pattern as `Falcon4SharedMemoryReader`.
- **DCS-BIOS addresses** (`config/dcs-bios-fields.sample.json`): intentionally shipped as
  placeholders (`Address: 0`). DCS-BIOS addresses are aircraft-module-specific and documented
  per-module by DCS-BIOS itself - see `docs/DCS_SETUP.md` for exactly where to find them.
- **Falcon 4 BMS input** (`Falcon4KeyboardCommandSender`) works by replaying global Windows
  keystrokes matching whatever key BMS has bound to a function - it only works while BMS is the
  foreground window, and only covers discrete/momentary functions (not analog axes). If a CAN
  panel switch needs to drive an analog axis in either sim, expose that axis as a USB HID
  joystick/throttle from the STM32 board directly instead - this app's input path is for
  buttons/switches/selectors.
- The STM32 side isn't included as firmware - see `firmware/stm32-slcan-notes/README.md` for
  exactly what your STM32 firmware needs to implement (the SLCAN command set) and pointers to
  existing open-source starting points, plus a `PcanBasicAdapter` alternative if you'd rather
  reach the STM32 nodes through a PEAK PCAN-USB dongle instead of a custom USB-CDC bridge. If your
  firmware speaks CANopen instead of raw frames, see `firmware/stm32-canopen-notes/README.md` too.

None of this changes the architecture - `MappingEngine`, both connectors, and both CAN adapters
are fully implemented - it's specifically the *exact numeric addresses/offsets*, which are
inherently sim-version- and aircraft-specific, that need a verification pass against your setup.

## Extending

- Add more Falcon 4 signals: the built-in table (`Falcon4FieldMap.BuildDefault()`/
  `BuildSecondaryDefault()`, generated by `Falcon4NativeFieldMapBuilder`) already exposes almost
  every field BMS publishes - check the Live Telemetry tab for an existing signal by its native
  BMS field name (e.g. `kias`, `aauz`, `lightBits_MasterCaution`, `bearing_0`) before adding a new
  one. For something genuinely missing (e.g. from the not-yet-wired IntelliVibe/OSB/String/Drawing
  areas), add an entry to `config/falcon4-fields.sample.json` with `{Name, ByteOffset, Type, Scale,
  Offset}` - it's merged onto the built-in table by `Name` at startup.
- Add more DCS-BIOS signals/commands: extend `config/dcs-bios-fields.sample.json` with entries
  copied from your aircraft's DCS-BIOS control reference.
- Add more BMS key bindings for CanToSim input: extend `config/falcon4-keybindings.sample.json`.
- Add a different CAN transport: implement `ICanBusAdapter` (see `SlcanSerialAdapter` for the
  shape) and wire it into `MainViewModel.ConnectCanAsync`.
- Extend CANopen support: `FalconCanBridge.Core/CanOpen/` is a small, self-contained set of classes
  (`CanOpenCobId`, `CanOpenNmtMaster`, `CanOpenHeartbeatMonitor`, `CanOpenSdoClient`) with no WPF/UI
  dependency - segmented SDO transfers or a SYNC producer, for instance, would both be a new class
  in that folder plus a small hook in `MainViewModel`, not a redesign.
