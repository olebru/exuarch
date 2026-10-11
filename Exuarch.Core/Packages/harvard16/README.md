# HARVARD-16

A machine with **three buses**, to show what you gain when a computer can move more than one value per tick. Instructions, data and devices each have their own bus, joined by two bridge registers.

## The idea

In a **Harvard architecture** the program and the data are held in separate memories on separate paths, so fetching the next instruction never has to wait for a data access. Microcontrollers and signal processors are often built this way.

HARVARD-16 uses that freedom: most instructions fetch their *successor* on the instruction bus in the same ticks as they finish their own work on the data bus. An ALU instruction takes two ticks including fetch, where a single bus machine needs three or more.

## The parts

- **ibus**, the instruction bus: [PC](exuarch:device/pc), [program memory](exuarch:device/pmem) and [the instruction register](exuarch:device/ir).
- **dbus**, the data bus: [A](exuarch:device/a), [B](exuarch:device/b), [the ALU](exuarch:device/alu) with its own operand latch [T](exuarch:device/t), [the flags](exuarch:device/flags), [data memory](exuarch:device/dmem) and [SP](exuarch:device/sp) for the stack.
- **iobus**, the device bus: [LCD](exuarch:device/lcd), [screen](exuarch:device/fb) and [keypad](exuarch:device/keys).
- Two bridges connect them: [opr](exuarch:device/opr) between ibus and dbus, and [io](exuarch:device/io) between dbus and iobus. A value crosses a bridge by being loaded on one side and put out on the other.

## How an instruction runs

The [fetch routine](exuarch:instruction/FETCH) also steps the PC past the opcode, so an instruction starts with the PC already on its operand, or on the next opcode.

[ADD](exuarch:instruction/ADD) shows the overlap. In its first tick B goes to the ALU's latch on dbus *while* the PC addresses the next opcode on ibus. In its second tick the ALU puts A + B into A on dbus *while* program memory loads the next opcode into the instruction register on ibus. There is no separate fetch at all.

[CALL](exuarch:instruction/CALL) is the showpiece: in the same tick the return address crosses the bridge to the stack on dbus and the target address goes into the PC on ibus.

Constants and strings are stored with the program, on the other bus from the data, so [LPM](exuarch:instruction/LPM) (load from program memory, as on AVR microcontrollers) brings them across the bridge.

## Things to try

1. Step [Hello, world across three buses](<exuarch:program/Hello, world across three buses>) with **Tick** in the [Run view](exuarch:tab/Run): during `OUT` the character travels on iobus while the next opcode is fetched on ibus. Look for two bus values in one tick.
2. Play the [Paddle game](<exuarch:program/Paddle game>): ⚡ Max, capture the keyboard on the keypad panel, and keep the ball in play.
3. Compare cycle counts: [Fibonacci on the LCD](<exuarch:program/Fibonacci on the LCD>) does the same work as BYOC-16's version. Which machine finishes in fewer ticks?

## Read more

- [Bridges between buses](exuarch:guide/bridges): how the bridge registers move values between buses.
- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks): why each bus can carry its own transfer in the same tick.
- [Memory and banks](exuarch:guide/memory-and-banks): program and data memory.
