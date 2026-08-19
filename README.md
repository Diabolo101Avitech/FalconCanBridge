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
| `FalconCanBridge.Core` | Shared models (`CanFrame`, `TelemetrySnapshot`, `SignalMapping`), the `ISimulatorConnector`/`ICanBusAdapter` interfaces, and `MappingEngine` (the bidirectional signal <-> CAN-frame packing/unpacking engine). |
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
   mapping and your STM32 firmware agree on IDs/byte layout.
5. **Log tab**: connection/adapter status and errors.

## Known limitations - please read before wiring real hardware

This was built and reviewed without access to a live BMS/DCS/STM32 rig to test end-to-end, so
two pieces are deliberately **configuration, not hardcoded assumptions**, and are marked as
best-effort:

- **Falcon 4 BMS field offsets** (`config/falcon4-fields.sample.json`, loaded by
  `Falcon4Connector`): the byte offsets for the default signal set (position, attitude,
  airspeed, altitude, ...) reflect the classic/commonly-reproduced layout of BMS's
  `FlightData` shared-memory block, but BMS shared memory is only authoritatively defined by
  `FalconSharedMemoryArea.h`, shipped with your BMS install, and by the community-maintained
  `F4SharedMem` reference project. **Verify offsets against one of those sources** (or watch the
  Live Telemetry tab while moving known controls, e.g. altitude on the runway should read ~0)
  before trusting values on real hardware. The whole point of driving this off a JSON file
  instead of a compiled struct is that you can correct/extend it without touching code.
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
  reach the STM32 nodes through a PEAK PCAN-USB dongle instead of a custom USB-CDC bridge.

None of this changes the architecture - `MappingEngine`, both connectors, and both CAN adapters
are fully implemented - it's specifically the *exact numeric addresses/offsets*, which are
inherently sim-version- and aircraft-specific, that need a verification pass against your setup.

## Extending

- Add more Falcon 4 signals: extend `config/falcon4-fields.sample.json` (or
  `Falcon4FieldMap.BuildDefault()`) with more `{Name, ByteOffset, Type, Scale, Offset}` entries.
- Add more DCS-BIOS signals/commands: extend `config/dcs-bios-fields.sample.json` with entries
  copied from your aircraft's DCS-BIOS control reference.
- Add more BMS key bindings for CanToSim input: extend `config/falcon4-keybindings.sample.json`.
- Add a different CAN transport: implement `ICanBusAdapter` (see `SlcanSerialAdapter` for the
  shape) and wire it into `MainViewModel.ConnectCanAsync`.
