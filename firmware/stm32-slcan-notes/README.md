# STM32 firmware notes (SLCAN)

The Windows app's default CAN adapter (`SlcanSerialAdapter`) speaks **SLCAN**, a.k.a. the
LAWICEL CANUSB ASCII-over-serial protocol - an open, widely implemented standard, not a custom
protocol invented for this project. Implementing it on your STM32 board means any USB-CDC
capable STM32 (F1/F4/F072/G4/H7 with bxCAN or FDCAN, ...) can act as the bridge between the PC
and your CAN-connected panel nodes with no vendor SDK required on the PC side.

This folder intentionally does **not** ship a full firmware project (that's a separate,
board-specific effort - clock tree, CAN transceiver wiring, USB descriptors, etc. all depend on
your exact STM32 part and board). Instead, here's what your firmware needs to implement to work
with `SlcanSerialAdapter` out of the box, plus where to start from existing open-source code
rather than from scratch.

## Where to start

Several open-source STM32 CAN-USB projects already implement SLCAN and are a much faster
starting point than a from-scratch bring-up:

- **CANable / candleLight hardware** (STM32F072-based) has an alternative "slcan" firmware
  build (as opposed to its default gs_usb/candleLight firmware) - search for the CANable
  project's slcan firmware branch/releases.
- Several STM32 "USB-CAN" hobbyist projects on GitHub implement the same LAWICEL command set
  against STM32 bxCAN/FDCAN peripherals and USB CDC ACM - useful as a peripheral-driver
  reference even if you end up writing your own command parser.

Cross-check any firmware you adopt against the exact command set below, since some
implementations only support a subset.

## Required serial commands (all lines terminated with CR, `0x0D`)

| Command | Meaning |
|---|---|
| `O\r` | Open the CAN channel (start normal operation) |
| `C\r` | Close the CAN channel |
| `S0\r` .. `S8\r` | Set bitrate preset: S0=10k, S1=20k, S2=50k, S3=100k, S4=125k, S5=250k, S6=500k, S7=800k, S8=1M bit/s |
| `t<id3><dlc><data>\r` | Transmit standard (11-bit) data frame - id as 3 hex chars, dlc as 1 hex char, data as `dlc*2` hex chars |
| `T<id8><dlc><data>\r` | Transmit extended (29-bit) data frame - id as 8 hex chars |
| `r<id3><dlc>\r` | Transmit standard remote frame (optional - the PC app doesn't require RTR support for typical panel I/O) |
| `R<id8><dlc>\r` | Transmit extended remote frame (optional) |

On receiving a CAN frame from the bus, the firmware should push a `t`/`T` line (same format as
above) out over USB-CDC as soon as possible - `SlcanSerialAdapter` treats every complete line as
one received frame. If a command is malformed or rejected, send a single bell character
(`0x07`) instead of a normal response line; the PC-side adapter logs a warning and moves on.

`SlcanSerialAdapter` sends `C\r`, then the `S<n>\r` bitrate command, then `O\r` on connect, and
`C\r` on disconnect - make sure your parser accepts `C` even when the channel isn't currently
open (should be a no-op, not an error).

## Practical firmware notes

- Drain the CAN peripheral's receive FIFO from an interrupt (or DMA) handler, not from the main
  loop, and hand frames off through a small ring buffer to whatever formats/sends the USB-CDC
  line - a busy CAN bus (e.g. many switches changing quickly) will overrun a single-depth
  hardware FIFO if you only poll it occasionally.
- Match the serial baud rate your firmware advertises over USB-CDC to what you configure in the
  PC app's connection string (`COM5;500000;115200` -> the `115200` is the serial link speed,
  independent of the `500000` CAN bitrate). USB-CDC "baud rate" is largely nominal since it
  rides over USB full-speed bulk endpoints, but keep the two consistent so nothing downstream
  assumes a mismatched value.
- Keep individual CAN frame -> panel-node mapping symmetric with what you configure in the PC
  app's `config/mapping.sample.json`: whatever CAN ID/byte offsets your panel nodes use for
  switches and gauges must match the `CanId`/`ByteOffset`/`BitOffset` values in the mapping
  profile, in both directions.
- If your panel has more than one physical STM32 node (e.g. one per sub-panel), those nodes talk
  to each other over the CAN bus as usual; only the node that's also USB-attached to the PC
  needs to implement SLCAN - the others just need ordinary CAN application firmware.

## Alternative: PCAN-Basic

If you'd rather keep the STM32 nodes as "plain" CAN nodes and reach the bus from the PC through
a PEAK PCAN-USB dongle instead of a custom STM32 USB-CDC bridge, use the `PcanBasicAdapter`
included in `FalconCanBridge.CanBus` instead of `SlcanSerialAdapter` - no firmware changes to
your STM32 nodes are needed for that path, but you do need PEAK's free "PCAN-Basic" API package
installed on the PC.
