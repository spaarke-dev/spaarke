---
status: test
disclosureId: disclosure-02
sourcePublication: US 6,413,802 B1
sourceTitle: FinFET transistor structures having a double gate channel extending vertically from a substrate and methods of manufacture
sourceGranted: 2002-07-02
sourceRecord: public (granted US patent)
inventionType: semiconductor device and method of manufacture (not software)
attorneyReviewed: false
---

# Test disclosure 02: a double-gate transistor whose channel is a vertical fin

> Test content for Phase 0 (spec FR-09). This is a short invention summary written for testing from the public record of the patent named above. It is not the inventors' text and does not reproduce the patent's claims. Draft from this disclosure only; do not look up or recall the published claims.

## Problem

As planar MOSFETs shrink, the gate loses control of the channel: the drain's field reaches under the gate, the threshold voltage drops with channel length, and the transistor leaks when it should be off (short-channel effects). Putting a gate on both sides of a very thin channel restores control, but earlier double-gate designs were hard to build because the second gate sat under the channel, where it is difficult to align with the first.

## The idea

Make the channel a thin, upright fin of silicon standing on an insulating layer, and put the gate over the fin so that it covers both side walls at once. The two side-wall gates are formed in the same step, so they are self-aligned to each other. An insulating cap left on top of the fin keeps the top surface from acting as a third gate, so the device is a double-gate transistor with the current flowing along the fin, parallel to the substrate.

Key parts:
- **Substrate and insulator:** a silicon wafer with a buried insulating layer (silicon-on-insulator).
- **Fin:** a narrow ridge cut from the top silicon layer. Its width is small compared with the gate length, which is what suppresses the short-channel effects. Its height sets the effective channel width.
- **Cap:** an insulating hard-mask layer left on top of the fin.
- **Gate dielectric:** a thin insulating film on the two side walls of the fin.
- **Gate:** a conductive gate layer (for example polysilicon, silicon-germanium, or a refractory metal or metal compound) patterned as a line that crosses over the fin, covering both side walls.
- **Source and drain:** wider doped islands at the two ends of the fin, on either side of the gate, for low-resistance contacts.

## How it is made (one embodiment)

1. Start with a silicon-on-insulator wafer (a silicon layer over a buried oxide) and form an insulating hard-mask layer on the top silicon.
2. Pattern the hard mask and etch the top silicon to leave the fin standing on the buried insulator, with the hard mask still on top, together with wider source and drain islands at the two ends of the fin.
3. Grow or deposit the gate dielectric on the exposed side walls of the fin.
4. Deposit the gate material and pattern it into a gate line crossing the fin.
5. Form spacers beside the gate, then dope the source and drain islands (and the gate).
6. Form contacts and the rest of the interconnect with ordinary planar MOSFET steps.

The process uses standard planar fabrication steps, so it fits an existing line.

## Variations described

- Several parallel fins running between one source and one drain, to multiply the drive current of one transistor.
- An N-type and a P-type channel stacked one above the other, separated by a dielectric layer and sharing one common gate, forming a compact CMOS pair.
- A choice of gate materials, so the gate's work function can help set the threshold voltage.

## What the inventors consider new

A double-gate field-effect transistor whose channel is a thin vertical fin on an insulator with a self-aligned gate covering both fin side walls and an insulating cap on top, and a manufacturing method for it that uses conventional planar process steps.
