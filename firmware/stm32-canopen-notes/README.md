# STM32 firmware notes (CANopen)

This is the firmware-side counterpart to the app's optional CANopen layer (see README.md
"CANopen support"). It only applies if you tick **Enable CANopen** in the Connections tab; if your
STM32 firmware just talks raw custom CAN frames, ignore this folder and see
`../stm32-slcan-notes/README.md` instead - that transport-level SLCAN doc still applies either way,
since CANopen is a payload-level protocol layered on top of whichever `ICanBusAdapter` transport
you're using (SLCAN USB-CDC or a PCAN-USB dongle), not a replacement for it.

As with the rest of this repo, no firmware project is shipped here - what follows is what your
STM32 firmware needs to implement to interoperate with `FalconCanBridge.Core.CanOpen`'s master-side
implementation, plus where to start instead of writing a CANopen stack from scratch.

## Where to start

Writing a compliant CANopen slave stack from scratch (object dictionary, SDO server, PDO
mapping, NMT state machine) is a much bigger undertaking than SLCAN - strongly prefer an existing
open-source stack over a bespoke one:

- **CANopenNode** (github.com/CANopenNode/CANopenNode) is a widely used, actively maintained,
  MIT-licensed CANopen stack in portable C, with an STM32 HAL/bxCAN port and example projects -
  the most direct starting point for a new STM32 CANopen node.
- Several vendor CAN/CANopen stacks (e.g. from STM32Cube expansion packages) provide similar
  functionality if you'd rather stay entirely inside STM32CubeIDE/CubeMX tooling.

Whichever stack you pick, cross-check its default object dictionary and PDO mapping against what
you configure in `config/mapping.sample.json` on the PC side - the two need to agree on which
CAN ID/byte offset carries which value, exactly like the non-CANopen SLCAN path.

## What your firmware needs to implement

- **NMT slave state machine** (CiA 301 §7.3.2): react to NMT commands on COB-ID `0x000` (byte 0 =
  command, byte 1 = your node ID or 0 for broadcast) - Start (`0x01`) -> Operational, Stop
  (`0x02`) -> Stopped, Enter Pre-operational (`0x80`), Reset Node (`0x81`) / Reset Communication
  (`0x82`). **PDOs are only exchanged while your node is in the Operational state** - if
  `FalconCanBridge`'s "auto-start on Open" is off, or fails, your node needs to reach Operational
  some other way (many simple firmwares just self-transition on boot instead of waiting for a
  master command; either is fine as long as the PC-side and firmware agree).
- **Heartbeat producer** (CiA 301 §7.2.8.3.1): send a 1-byte frame on COB-ID `0x700 + NodeID`
  periodically (commonly every 1000 ms) with your current NMT state: `0x00` once at boot
  ("Boot-up"), then `0x04` (Stopped), `0x05` (Operational), or `0x7F` (Pre-operational) on every
  subsequent heartbeat. Without this, the PC app's node-status indicator never leaves "waiting for
  heartbeat..." and eventually can't tell your node apart from one that's simply offline.
- **PDO producer/consumer** at the predefined connection set COB-IDs (`0x180/0x200 + NodeID` for
  PDO 1, `0x280/0x300` for PDO 2, `0x380/0x400` for PDO 3, `0x480/0x500` for PDO 4 - Txxx = your
  node transmits, Rxxx = your node receives) - see the table in the main README. Keep these
  **asynchronous/event- or timer-driven**, not SYNC-gated: `FalconCanBridge` never sends a SYNC
  frame (COB-ID `0x080`), so a PDO configured to only transmit on SYNC will never fire towards the
  PC.
- **Expedited SDO server** on `0x600+NodeID` (requests) / `0x580+NodeID` (responses) *if* you want
  the PC app's SDO Read/Write test panel to work against your object dictionary - only needed for
  configuration-style values you don't already expose via PDO. Values up to 4 bytes only; the PC
  side (`CanOpenSdoClient`) doesn't implement segmented transfers.

## Practical firmware notes

- A node ID is required and must be configured some way the PC side can match (DIP switches, a
  compiled-in constant, a value read from flash, ...) - this app has no LSS (auto node-ID
  assignment) support, so whatever you pick needs to be entered as the "Node ID" in the
  Connections tab.
- If several physical STM32 nodes share the bus (e.g. one per sub-panel), each one that should be
  independently visible to the PC app (separate heartbeat/PDOs/SDO) needs its own distinct node ID
  and its own heartbeat producer - only the node that's also USB-attached to the PC needs to run
  the SLCAN transport layer itself (see `../stm32-slcan-notes/README.md`), but CANopen NMT/PDO/SDO
  behavior is per logical node regardless of which one is physically USB-attached.
- Keep your heartbeat producer time comfortably faster than the PC app's consumer timeout (2000 ms
  by default) - a 1000 ms producer time leaves headroom for the occasional missed/delayed frame
  without flapping the status indicator between "Operational" and "heartbeat lost".
