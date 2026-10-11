# BEN-16

Named for Ben Eater, whose videos build an 8 bit computer on breadboards one module at a time. BEN-16 is a 16 bit version of that computer, which follows the SAP-1 (Simple As Possible) design from Malvino's *Digital Computer Electronics*. It has the same modules on one shared bus, the same control signals and the same instruction set. Every instruction takes the same five clock steps.

## The modules

The hardware design lays out the two columns of the breadboard build, one above the bus and one below it:

- Above the bus are the [CLOCK](exuarch:device/clk), the [RAM](exuarch:device/mem) with its address register, the [INSTRUCTION REGISTER](exuarch:device/iw) and the [STEP COUNTER](exuarch:device/ir), whose microcode takes the place of the control logic EEPROMs.
- Below the bus are the [PROGRAM COUNTER](exuarch:device/pc), the [A REGISTER](exuarch:device/a), the [ALU](exuarch:device/alu), the [B REGISTER](exuarch:device/b), the [OUTPUT](exuarch:device/out) register and the [FLAGS](exuarch:device/flags).

| Breadboard signal | Here |
|---|---|
| CO, CE, J | `pc.output`, `pc.inc`, `pc.load` |
| MI, RO, RI | `mem.loadmar`, `mem.output`, `mem.load` |
| II, IO | `iw.load` with `ir.load`, `iw.field0` |
| AI, AO, BI | `a.load`, `a.output`, `b.load` |
| EO, EO with SU | `alu.add`, `alu.sub` |
| OI | `out.load` |
| HLT | `clk.disable` |

## The instruction word

The 8 bit build splits each instruction byte into a 4 bit opcode and a 4 bit address, which limits it to 16 bytes of RAM. Here the word is 16 bits: a 4 bit opcode and a 12 bit address or value, so the RAM holds 4096 words, and `LDI` loads 0 to 4095.

## Five steps

- **T0 and T1** fetch: `CO MI`, then `RO II CE`.
- **T2 to T4** belong to the instruction: `ADD` is `IO MI`, `RO BI`, `EO AI`.
- Every instruction pads with empty steps to T4, where the step counter resets, as the 8 bit build's counter does. `HLT` is the only exception.

## Differences from the 8 bit build

- **Carry after SUB.** The ALU here sets C on a borrow, when A < B. The 8 bit build adds the two's complement and sets C when there is no borrow. The Multiply program therefore leaves its loop on `JC` after `SUB`.
- **Opcode numbers.** The instruction list numbers opcodes in order from 1. LDA to JZ keep their 8 bit codes 1 to 8, OUT and HLT are 9 and 10 instead of 14 and 15, and NOP is 11. A word of 0 runs fetch again, so it also does nothing.
- **Flags.** The ALU writes the flags on every ADD and SUB, which is when the 8 bit build turns on FI.
- **The output.** OUTPUT is a register shown in Run, not a seven segment display.

## Programs

- <exuarch:program/Add two numbers>: 28 + 14 = 42.
- <exuarch:program/Count up and down>: up in steps of 256 until the sum carries, then down to 0.
- <exuarch:program/Multiply>: 123 × 45 = 5535 by repeated addition.
- <exuarch:program/Fibonacci>: up to 46368, the largest Fibonacci number that fits in 16 bits.

## Things to try

1. Load [Add two numbers](<exuarch:program/Add two numbers>) and step it with **Tick** in the [Run view](exuarch:tab/Run). Every instruction takes five ticks: two to fetch it and three to carry it out.
2. Run [Multiply](<exuarch:program/Multiply>) and watch the carry flag. The loop ends on [JC](exuarch:instruction/JC) after a [SUB](exuarch:instruction/SUB) borrows, where the 8 bit build tests for no carry.
3. Open the [Decoder ROM](exuarch:tab/Run) panel under the schematic while stepping: each tick is one row of it, and the empty steps that pad an instruction to T4 are empty rows.
4. Change something: the 4 bit opcode leaves four opcodes free, 12 to 15. Add an instruction of your own in one, and see what the microcode editor reports.

## Read more

- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register): how fetch and the instruction register step through an instruction.
- [Flags and conditions](exuarch:guide/flags-and-conditions): how JC and JZ select their steps.
- [Registers and the ALU](exuarch:guide/registers-and-the-alu): a tutorial that builds a machine like this one from scratch.
