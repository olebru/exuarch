# MOVE-16

A computer with **no instruction set**, only moves. This is a *transport triggered architecture*, an idea explored in research processors such as MOVE: instead of instructions that specify what to compute, the program only specifies where to move data, and the units act on what arrives.

## The idea

Every instruction has the same form: move a value from a **source port** to a **destination port**, written `SOURCE_TO_DESTINATION`. Moving a value into the `ADD` port *is* the addition. Moving a value into `JZ` *is* the conditional jump. Moving anything into `HALT` stops the machine.

Because the only thing the hardware ever does is transport a value, the instruction decoder is trivial, and every unit can be added or removed without changing the "instruction set". There are 218 moves, one for each source and destination pair except moving a register to itself, `MEM_TO_MEM` and `R3_TO_CALL`.

## The ports

Sources, where a value can come from:

- `IMM`, the number written after the move.
- `R0` to `R3`, four registers ([R0](exuarch:device/r0), [R1](exuarch:device/r1), [R2](exuarch:device/r2), [R3](exuarch:device/r3)).
- `RES`, [the last result](exuarch:device/res) of the function unit.
- `MEM`, [memory](exuarch:device/mem) at the address last moved to `MAR`.
- `KEYS`, [the keypad](exuarch:device/keys).

Destinations, where a value can go:

- The registers, `MAR`, which sets the address `MEM` reads and writes, `MEM` itself, and `A`, [the function unit's first input](exuarch:device/opa).
- **Triggers**: `ADD`, `SUB`, `AND`, `OR`, `XOR`, `SHL` and `SHR` combine A with the arriving value in [the function unit](exuarch:device/alu) and leave the result in `RES`; `CMP` only sets the flags.
- **Jumps**: `JMP`, `JZ`, `JNZ`, `JN` and `JC`, and `CALL`, which also leaves the return address in R3.
- **Devices**: `LCD`, `X`, `Y` and `PIXEL`, and the value-ignored ports `CLEAR`, `CLS` and `HALT`.

## How an instruction runs

Adding 1 to R1 takes three moves:

```
R1_TO_A
IMM_TO_ADD 1
RES_TO_R1
```

Look at [IMM_TO_ADD](exuarch:instruction/IMM_TO_ADD) in the microcode: it reads the number after the opcode into the ALU's trigger latch, then the ALU adds and the result goes to RES. [R1_TO_A](exuarch:instruction/R1_TO_A) is a single step, one value on the bus.

## Things to try

1. Read [Hello, world with nothing but moves](<exuarch:program/Hello, world with nothing but moves>) first: comparing, jumping and printing are all moves.
2. [Count from 00 to 99](<exuarch:program/Count from 00 to 99>) calls a subroutine by moving into `CALL` and returns with `R3_TO_JMP`.
3. [XOR texture](<exuarch:program/XOR texture>) is slow on purpose, a dozen moves per value. Run it at ⚡ Max and watch the [screen](exuarch:tab/Run) fill.
4. Add a port: a `MUL` trigger would be one more destination, and every source would have a move into it.

## Read more

- [Microcode](exuarch:guide/microcode): what the moves are made of.
- [Devices and control lines](exuarch:guide/devices-and-control-lines): the devices behind the ports.
- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks): how one move happens in a tick.
