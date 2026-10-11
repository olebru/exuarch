# Buses and the two-phase tick

Everything in an ExµArch machine happens in clock ticks, and almost everything moves over a bus. A bus is a shared set of 16 wires that devices put values on and take values from. A tick is one beat of the clock, in which every bus carries at most one value from one device to any number of others. Knowing exactly what a bus does within a tick is what lets you predict what your microcode will do.

## A bus

A bus has an id, such as `main`, and nothing else to configure: every bus is 16 bits wide, like every register and memory cell. Add one with **＋ Bus** in the [hardware design](<exuarch:tab/Hardware design>), then connect device ports to it.

Three rules hold for every bus in every tick:

- **At most one device drives it.** Two devices putting a value on the same bus in the same tick is a short circuit. The microcode editor reports it as an error before you run, and if it happens anyway the machine stops with a "puff of blue smoke" error naming both devices.
- **Any number of devices can read it.** A register loading, a memory storing and a display printing can all take the same value in one tick.
- **A bus nobody drives reads as 0.** At the start of every tick each bus is cleared. If a step reads a bus that nothing drives, the read returns 0, and the microcode editor warns you about it.

A value is only on a bus for the tick it is driven. Nothing stays on the wires into the next tick; values are kept in registers and memory.

## The two halves of a tick

A tick runs in two halves, and every device takes part in both:

1. **Drive.** Every device whose output line is on puts its value onto its bus. Registers put out their contents, memory puts out the cell at its address, the ALU puts out its result.
2. **Latch.** Every device whose input line is on takes the value from its bus and stores it. Registers load, memory stores, the instruction register moves on.

Because all driving happens before any latching, a device always reads the value its bus carries in this tick, and the order in which devices are listed never matters. It also means a value can not travel through two devices in one tick: what a register loads at the end of a tick is only available to drive in the next one.

A typical pair of steps shows this. Reading the memory cell the program counter points at takes two ticks:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, ir.load
```

In step 1 the program counter drives the bus and the memory latches it as its address. In step 2 the memory drives the cell at that address, and the instruction register latches it. Putting `mem.output` into step 1 would read the cell at the old address, because the new one is only stored at the end of the tick.

Operations that change a device in place, such as a register's `inc`, also happen in the latch half, so `pc.output` and `pc.inc` in the same step put the old value on the bus and then count up.

## More than one bus

Each bus follows the rules above on its own, and all buses tick together. So two transfers on two different buses can happen in the same step, which is how a machine with several buses gets more done per tick. In [HARVARD-16](exuarch:package/HARVARD-16) the program memory sits on `ibus` and the data memory on `dbus`, so the last step of `POP` does both at once:

```
dmem.output, a.load, sp.inc, pmem.output, ir.load, pc.inc
```

On `dbus` the data memory drives the top of the stack into A; on `ibus` the program memory drives the next opcode into the instruction register. A [bridge](exuarch:guide/bridges) register, sitting on two buses, is how a value moves from one to the other.

## The clock

The `clock` device has no bus ports. It counts ticks, and it has one control line, `disable`, which stops it. A machine names its clock in the `halt` setting of its definition; when that clock is disabled the machine is halted, and **Tick**, **Instruction** and **Run** in the [Run](exuarch:tab/Run) tab do nothing until you reset. A `HLT` instruction is usually a single step with `clk.disable`.

A machine without a `halt` clock never halts on its own; you stop it by pausing.

## See also

- [Devices and control lines](exuarch:guide/devices-and-control-lines)
- [Microcode](exuarch:guide/microcode)
- [Bridges](exuarch:guide/bridges)
- [The clock device](exuarch:reference/clock)
- [HARVARD-16](exuarch:package/HARVARD-16), three buses working in parallel
