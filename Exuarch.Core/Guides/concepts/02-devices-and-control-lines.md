# Devices and control lines

A machine is a set of devices wired to buses: registers, memory, an ALU, a display, a clock. Devices do nothing on their own. Each one has a handful of control lines, and a device only acts in a tick when microcode turns one of its lines on. The devices and their lines are the vocabulary your microcode is written in.

## Types and ids

Every device has a **type**, which determines what it can do, and an **id**, which is how microcode and other devices refer to it. You add a device by dragging its type from the palette in the [hardware design](<exuarch:tab/Hardware design>) onto the canvas. The palette groups the types:

- **Registers**: `register`, `statusRegister`, `dualPortRegister`
- **Control**: `instructionRegister`, `clock`, `interruptController`
- **Arithmetic**: `alu`, `mac`
- **Memory**: `ram`, `mmu`
- **I/O**: `display`, `framebuffer`, `rasterizer`, `depthBuffer`, `blitter`, `timer`, `keypad`

Below them, **Buses** adds a bus, and for a machine without a decoder the palette includes a **Decoder** group to add one.

A new device is given an id from its type, such as `reg`, `reg2` and so on. Select it to change the id in the inspector; an id is one word without spaces and must not clash with another device or a bus. Renaming a device updates every signal in the microcode, every connection and every decoder setting that uses the old id. The display name is only a label for the canvas.

## Ports, connections and parameters

A device has up to three kinds of wiring, all shown in the inspector:

- **Bus ports** connect it to buses. Most types have one port, `data`. A `dualPortRegister` has `a` and `b`, a `blitter` has `host` and `video`, and a `rasterizer` has `host`, `list` and `video`. A clock has none.
- **Connections** point at other devices it works with directly, not over a bus. An `alu` is connected to the registers it reads as `a` and `b` and to the register that receives its flags as `status`.
- **Parameters** set it up: a memory's `size`, a display's `columns` and `rows`, a timer's `period`. Registers have none: every register starts at 0.

In the machine's JSON (under *This machine* in the getting started drawer) the same device reads:

```json
{
  "id": "alu",
  "type": "alu",
  "bus": "main",
  "connections": { "a": "rega", "b": "regb", "status": "regsta" }
}
```

`bus` is the short form for the `data` port; a device with other ports lists them under `buses`, as in `"buses": { "a": "ibus", "b": "dbus" }`. The machine fails to build when a type is unknown, a port names a bus that does not exist, or a connection names a device that does not exist, and the hardware design lists these problems.

## Control lines

A control line is written `device.line`: `pc.output`, `mem.loadmar`, `alu.add`. Each line of a type does one of three things, and the microcode editor marks which:

- **Drives a bus** (▲): puts a value on the bus of one of the device's ports during the tick, like a register's `output`.
- **Reads a bus** (▼): takes the value from a port's bus at the end of the tick, like a register's `load`.
- **Internal**: changes the device without using a bus, like a register's `inc`, `dec` and `reset`, or the clock's `disable`.

A line is on for exactly one tick. The decoder turns on the lines listed in the current micro step, the tick runs, and every line turns off again. If you want a register to load on three ticks in a row, three steps must each include `load`. See [Buses and the two-phase tick](exuarch:guide/buses-and-ticks) for what happens within that tick.

Several lines of the same device can be on in one step. A register latches `load` first and then applies `reset`, `inc` and `dec`, so `load` and `inc` together store the bus value plus one. The exceptions are the ALU and the multiply-accumulate unit, which do one operation per tick.

## What each type offers

The [device reference](exuarch:reference/register) has a page per type with its lines, ports, connections and parameters. Some types work with the decoder rather than as ordinary devices: the machine's decoder names one `instructionRegister` as its micro step counter and one register as its status register, and optionally an `interruptController`. A `clock` named as `halt` stops the machine, and a memory named as `programMemory` receives the assembled program.

The inspector also lists, for the selected device, which instructions use its lines, so you can see what a register is actually for.

## See also

- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks)
- [Microcode](exuarch:guide/microcode)
- [The ALU](exuarch:reference/alu)
- [Registers](exuarch:reference/register)
- [BYOC-16](exuarch:package/BYOC-16), a small set of devices on one bus
