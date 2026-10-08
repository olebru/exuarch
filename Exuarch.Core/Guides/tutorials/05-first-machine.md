# Your first machine

In this tutorial you give the machine from [Ending an instruction](exuarch:guide/ending-an-instruction) a display, and write the one instruction it needs to print. By the end it says "Hi". The next tutorials keep building on this machine, so keep it open.

## Start from the last tutorial

The machine is a program counter `pc`, a memory `mem`, the instruction register `ir`, a status register `status`, all on one bus called `main`, and a clock `clk`, which needs no bus. Its microcode has the fetch routine you wrote and three instructions: `HLT`, `NOP` and `JMP`. It runs, but it has no way to show anything.

If you skipped the first four tutorials, click **New…**, pick **Minimal CPU**, give it a name and click **Create**. It is the same machine, with the instructions in the order `NOP`, `JMP`, `HLT`, which changes their opcodes and nothing else.

## Add a display

1. Open [Hardware design](<exuarch:tab/Hardware design>).
2. Drag **display** from the palette onto the canvas, next to the bus, or click it in the palette. It connects to the nearest bus by itself.
3. With the new device selected, set its **ID** to `lcd` in the inspector on the right.

A [display](exuarch:reference/display) is a character LCD. It has one input line that matters here, `load`: in the tick it is on, the display takes the low 8 bits of the bus and prints them as a character at its cursor, then moves the cursor on. The ID is how microcode names the device, so its load line is now `lcd.load`.

## Write the OUT instruction

1. Open [Microcode](exuarch:tab/Microcode) and click **＋ Instruction**.
2. Set **Mnemonic** to `OUT`, **Operands** to 1 and the operand type to **value**.
3. Give it two steps. The new instruction starts with one step holding `ir.reset`, which would end it after that step: remove it, click **＋ Add step** for the second step, and put `ir.reset` at the end of that one, so the steps read:
   - step 1: `pc.output` `mem.loadmar`
   - step 2: `mem.output` `lcd.load` `pc.inc` `ir.reset`

Type a signal into the *add device.line* field of the selected step, or click it in the control lines palette. What the steps do:

- By the time `OUT`'s own steps start, the fetch routine has already loaded the opcode and moved the program counter past it, so `pc` points at the operand.
- **Step 1**: the program counter drives its value onto the bus (`pc.output`) and the memory takes it as the address to read (`mem.loadmar`).
- **Step 2**: the memory drives the operand onto the bus (`mem.output`) and the display prints it (`lcd.load`). In the same tick `pc.inc` steps past the operand, and `ir.reset` ends the instruction, so the next tick starts fetch again.

A step is one clock tick. In every tick the devices whose output line is on drive the bus first, and then the devices whose load lines are on take the value, which is why one step can both put a value on the bus and store it.

## Write the program

1. Open [Program](exuarch:tab/Program) and replace the starter program with:

```asm
        OUT 'H'
        OUT 'i'
        HLT
```

`'H'` is a character literal: the assembler stores its character code, 72, in the cell after `OUT`'s opcode. `OUT` comes after `HLT`, `NOP` and `JMP`, so its opcode is 6, and memory holds `0006 0048 0006 0069 0002`: each opcode, then its character, then `HLT`.

## Run it

1. Open [Run](exuarch:tab/Run) and press **Tick** (→) a few times.
2. Open the **Trace** tab below the machine.

Each row of the trace is one tick: the micro step that ran (such as `OUT.2`), what moved over the bus and which signals were on. The newest tick is at the top, so read it from the bottom up: each instruction starts with two rows of fetch, then come `OUT`'s two steps, and an `H` appears on the LCD. The whole program takes 11 ticks: four for each `OUT` (two of fetch and two of its own) and three for `HLT`, whose one step stops the clock.

Press **⟲ Reset** and try **Instruction** (Shift+→), which runs a whole instruction at a time.

## Forget pc.inc

Step 2's `pc.inc` moves the program counter past the operand. What happens without it?

1. Remove `pc.inc` from `OUT`'s step 2, reset, and run with **Space**.

The LCD shows `H`, and then nothing more. `pc` still points at the operand, so the next fetch reads the letter `H` as an opcode, 72, and the decoder jumps to ROM address 72. Nothing is there: every line stays off, nothing clears the counter, and it walks on through empty words for ever. The machine never halts. It is [Opcodes are addresses](exuarch:guide/opcodes-are-addresses) again: the machine cannot tell an opcode from a character.

2. Pause with **Space**, and put `pc.inc` back.

## Next

- [Registers and the ALU](exuarch:guide/registers-and-the-alu): give the machine registers and arithmetic.
- [Buses and ticks](exuarch:guide/buses-and-ticks) explains the two halves of a tick.
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register) explains how an opcode picks its steps.
