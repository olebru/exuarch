# Loops and flags

Watch this tutorial as a video: [episode 7 of Build a computer in ExµArch](https://www.youtube.com/watch?v=S5w4CEguKPE) on YouTube.

So far every program runs straight through. In this tutorial the machine from [Registers and the ALU](exuarch:guide/registers-and-the-alu) learns to jump back and repeat until a count runs out, and prints a countdown: `54321`.

Skipped a tutorial, or lost your machine? Load [Tutorial 7 · Loops and flags](<exuarch:package/Tutorial 7 · Loops and flags>), under **Tutorial** in **Machines**: it is the machine this tutorial starts from.

## Flags

Every ALU operation writes four flags into the status register: **Z** when the result is 0, **N** when it is negative, **C** for a carry (or a borrow when subtracting) and **V** for signed overflow. The status register feeds the decoder, and a step in the microcode can be limited to certain flags. That is the whole mechanism behind a conditional jump: the same opcode runs different steps depending on the flags.

## Write SUB and CMP

Open [Microcode](exuarch:tab/Microcode) and add two instructions without operands.

`SUB` subtracts `b` from `a`:

- step 1: `alu.sub` `a.load` `ir.reset`

`CMP` compares `a` with `b`:

- step 1: `alu.cmp` `ir.reset`

`alu.cmp` works out `a - b` and sets the flags, but drives nothing onto the bus, so no register changes. After `CMP`, Z is 1 exactly when `a` equals `b`.

## Write JNZ

`JNZ` jumps to its operand when Z is 0, "jump if not zero". Give it one operand of type **address**, and three steps:

- step 1: `pc.output` `mem.loadmar`
- step 2, only when Z=0: `mem.output` `pc.load` `ir.reset`
- step 3, only when Z=1: `pc.inc` `ir.reset`

To limit a step, click the **Z** button in its header: once for Z=1, twice for Z=0, a third time for any. Step 2 gets Z=0 and step 3 gets Z=1.

A step whose condition does not match is left out altogether. With Z=0, `JNZ` runs steps 1 and 2: it reads the operand and loads it into the program counter, the jump. With Z=1 it runs steps 1 and 3: it steps past the operand and carries on with the next instruction. Use **Preview flags** above the steps to see which steps run for each value of the flags.

Operand type **address** tells the assembler the operand is somewhere to go, so a label there is what you meant.

## Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
        LAI '5'
loop:   OUTA
        LBI 1
        SUB
        LBI '0'
        CMP
        JNZ loop
        HLT
```

`loop:` is a label: the name of the address of the instruction after it. `JNZ loop` assembles to `JNZ`'s opcode followed by that address.

Each time round, the program prints `a`, subtracts 1, and compares what is left with the character `'0'`. While they differ, Z is 0 and `JNZ` jumps back. When `a` reaches `'0'`, Z is 1, the jump falls through and `HLT` stops the clock.

## Run it

Open [Run](exuarch:tab/Run) and run it: the LCD shows `54321`. Step through the last time round with **Instruction** (Shift+→) and watch the status register: after the final `CMP` its Z flag is set, and the **Trace** shows `JNZ` running its step 3 instead of step 2.

Open the **Decoder ROM** tab to see where the steps live: the decoder ROM holds a copy of every instruction's steps for each combination of flags, and the flags pick which copy runs.

## Forget the condition

What does the Z=0 on step 2 really do?

1. Click step 2's **Z=0** button once, so it shows plain **Z**: the step now runs for any flags.
2. Reset and run.

The LCD shows `543210/.-` and keeps going: the countdown runs past `1`, prints `0`, and on through the characters below it. The machine never halts. Step 2 now runs whatever Z is, and it ends with `ir.reset`, so step 3 can never be reached: `JNZ` always jumps. A flag only makes a decision because a step is limited to it.

3. Set step 2 back to Z=0.

Things to try:

- Count down from `9`.
- Add `JZ`, "jump if zero", by swapping the two conditions.

## Next

- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack): call the same code from several places.
- [Flags and conditions](exuarch:guide/flags-and-conditions) covers the flags and the decoder ROM.
- [Operands](exuarch:guide/operands) covers values, addresses and labels.
