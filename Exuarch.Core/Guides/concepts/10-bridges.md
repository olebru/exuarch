# Bridges between buses

A bus carries one value per tick, driven by at most one device. That makes a single bus machine easy to follow, but it also means every transfer takes its own tick. Add a second bus and two transfers can happen in the same tick, one on each. A bridge is how the two halves then exchange data: a register that sits on both buses.

## The dualPortRegister

A [`dualPortRegister`](exuarch:reference/dualPortRegister) has two bus ports, `a` and `b`, which you connect to different buses in the hardware design. In the machine's JSON its ports are listed under `buses`:

```json
{ "id": "opr", "type": "dualPortRegister", "buses": { "a": "ibus", "b": "dbus" } }
```

It holds one 16 bit value, which starts at 0 like every register's. Its control lines are:

- `loada` and `loadb` take the value from bus a or bus b.
- `outputa` and `outputb` put the value on bus a or bus b.
- `reset`, `inc` and `dec` set it to 0, add 1 or subtract 1.

`loada` and `loadb` can not both be on in the same tick. The machine stops with an error if they are.

## Crossing takes two ticks

Within a tick every device drives first and latches second (see [buses and ticks](exuarch:guide/buses-and-ticks)). An output line puts out the value the register held when the tick began, and a load line changes it at the end. So a value crosses a bridge in two steps:

```json
{ "signals": ["a.output", "io.loada"] },
{ "signals": ["io.outputb", "lcd.load"] }
```

In the first step, A goes onto the data bus and into the bridge. In the second, the bridge puts it on the I/O bus, where the LCD takes it. You can also load one side and output the other in the same step. The output then carries the old value while the new one comes in, which lets a bridge pass a stream of values along one per tick.

## Why bother: HARVARD-16

[HARVARD-16](exuarch:package/HARVARD-16) has three buses: `ibus` for instructions, `dbus` for data and `iobus` for devices. Two bridges join them: `opr` between `ibus` and `dbus`, and `io` between `dbus` and `iobus`. Program memory is on `ibus` and data memory on `dbus`, so the machine keeps them apart, as a Harvard architecture does.

The payoff is overlap. Because each bus carries its own transfer, an instruction can do its own work on `dbus` or `iobus` while it fetches the next opcode on `ibus`. Here is its `OUT`, which prints A on the LCD:

```json
{ "signals": ["a.output", "io.loada", "pc.output", "pmem.loadmar"] },
{ "signals": ["io.outputb", "lcd.load", "pmem.output", "ir.load", "pc.inc"] }
```

Three transfers happen at once in these two ticks. A crosses to the I/O bus, the program counter addresses program memory, and the next opcode goes into the instruction register. The instruction is finished when its successor has already been fetched. That is why most of HARVARD-16's instructions, 38 of its 48, end with `ir.load` rather than `ir.reset`.

Operands take the other bridge. An immediate value is read from program memory on `ibus`, loaded into `opr` from side a, and put on `dbus` from side b in the next step. To read a table stored with the program, the direction is reversed: `LPM` loads an address into `opr` from `dbus`, outputs it on `ibus` to program memory, and brings the cell back across.

## Designing with bridges

- **One bridge per crossing direction you use often.** A bridge holds one value, so two transfers that need to cross at once need two bridges.
- **Mind which side each device is on.** A register on `dbus` can not reach a device on `iobus` in one step. Plan the extra tick in the microcode.
- **Look at the Run view.** Its **Last tick** panel lists every bus with the device that drove it, the value and the devices that read it, so you can see the buses working in the same tick.

You can split a machine into as many buses as you like. Nothing stops you from giving each device its own bus and a bridge for every path, although the microcode then has to move every value by hand.

## See also

- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks)
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [dualPortRegister reference](exuarch:reference/dualPortRegister)
- [HARVARD-16](exuarch:package/HARVARD-16)
