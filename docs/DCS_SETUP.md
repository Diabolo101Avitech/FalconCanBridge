# DCS World setup (DCS-BIOS)

`FalconCanBridge.Simulators.Dcs` talks to DCS through **DCS-BIOS**, the community export/command
framework - it does not hook DCS directly. You need DCS-BIOS installed once, independently of
this app:

1. Install DCS-BIOS following its own instructions: https://github.com/dcs-bios/dcs-bios
   (the installer/updater drops a `Scripts\DCS-BIOS` folder into your `Saved Games\DCS` (or
   `DCS.openbeta`) folder and adds a line to `Scripts\Export.lua` that loads it).
2. If you ever need to do that Export.lua wiring by hand, the line DCS-BIOS's own installer
   adds is of the form:

   ```lua
   local lfs = require('lfs')
   dofile(lfs.writedir()..[[Scripts\DCS-BIOS\BIOS.lua]])
   ```

   Prefer the official installer over typing this by hand - it also keeps the per-aircraft
   control-reference JSON files (see below) up to date across DCS-BIOS releases.
3. Start DCS, load into a plane. DCS-BIOS begins streaming export data over UDP - by default to
   multicast group `239.255.50.10:5010`, and it listens for command input on UDP port `7778`.
   `DcsBiosConnector` uses those same defaults; pass different values to its constructor if you
   changed DCS-BIOS's own `config.lua`.

## Finding the CAN mapping addresses for your aircraft

DCS-BIOS assigns every cockpit control and readout in every aircraft module a numeric
`address` (plus `mask`/`shift_by` for values packed into a shared word). Those addresses are
aircraft-specific and are **not** hardcoded in this app - they live in the per-aircraft
"control reference" JSON files DCS-BIOS ships (also browsable through the DCS-BIOS Hub web UI,
started via `Scripts\DCS-BIOS\StartupDCSBIOSHub.bat` shipped with DCS-BIOS).

For every DCS signal you want to drive a CAN gauge/light from, or every DCS control you want a
CAN panel switch to trigger:

1. Look the control up in the DCS-BIOS Hub / control reference JSON for your module.
2. Copy its `address` (and `mask`/`shift_by` for sub-word fields) into
   `config/dcs-bios-fields.sample.json` (rename/copy it first) as a
   `DcsBiosSignalDefinition` entry - remember DCS-BIOS documents addresses in hex, this app's
   JSON needs plain decimal (e.g. `0x1234` -> `4660`).
3. For output-only "Number"/"String" signals, reference the signal by `Name` from a
   `SimToCan` row in your mapping profile.
4. For inputs, DCS-BIOS controls are triggered by the *identifier* string itself (not a
   separate address) - put that identifier straight into a `CanToSim` mapping row's
   `CommandName`, e.g. `CommandName: "UFC_1"`, `CommandName: "LG_HANDLE"`.
