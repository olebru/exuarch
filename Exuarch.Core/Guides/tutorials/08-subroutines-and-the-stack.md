# Subroutines and the stack

A subroutine is code a program can call from several places and that returns to wherever it was called from. To return, the machine has to remember where it came from, and it keeps that in memory on a stack. In this tutorial you give the machine from [Loops and flags](exuarch:guide/loops-and-flags) a stack pointer and the instructions `CALL` and `RET`.

Skipped a tutorial, or lost your machine? Load [Tutorial 8 · Subroutines and the stack](<exuarch:package/Tutorial 8 · Subroutines and the stack>), under **Tutorial** in **Machines**: it is the machine this tutorial starts from.

## Add a stack pointer and a scratch register

1. Open [Hardware design](<exuarch:tab/Hardware design>) and drag two more **register** devices onto the bus.
2. Set their IDs to `sp` and `tmp`.

`sp` points at the top of the stack. Like every register it starts at 0, and the stack grows downwards from there: a push first moves `sp` down one cell and then writes there. Moving down from 0 wraps round to 65535, and because the memory holds 4096 cells it takes an address modulo 4096, so 65535 is cell 4095, the last one. The stack starts at the top of memory and grows away from the program at the bottom, without anything to set up. [Memory and banks](exuarch:guide/memory-and-banks) explains why that matches real hardware, where ExµArch makes it easier, and when it stops working. `tmp` is a scratch register that holds a value for a few ticks.

## Write CALL

`CALL` has one operand of type **address**, the subroutine to call, and five steps:

- step 1: `pc.output` `mem.loadmar`
- step 2: `mem.output` `tmp.load` `pc.inc` `sp.dec`
- step 3: `sp.output` `mem.loadmar`
- step 4: `pc.output` `mem.load`
- step 5: `tmp.output` `pc.load` `ir.reset`

What happens:

- **Steps 1 and 2** read the operand into `tmp`. The program counter then steps past the operand, so it holds the return address: the instruction after the `CALL`. In the same tick `sp.dec` moves the stack pointer down to a free cell.
- **Step 3** addresses that cell. **Step 4** writes the return address into it: `mem.load` stores the bus value in the addressed cell.
- **Step 5** jumps to the subroutine.

`tmp` is needed because the bus carries one value per tick: the subroutine's address has to wait somewhere while the return address is written.

## Write RET

`RET` has no operands and two steps:

- step 1: `sp.output` `mem.loadmar`
- step 2: `mem.output` `pc.load` `sp.inc` `ir.reset`

It reads the cell `sp` points at into the program counter, and moves `sp` back up, which pops the return address off the stack.

## Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
        LAI 'O'
        CALL twice
        LAI 'K'
        CALL twice
        HLT

twice:  OUTA
        OUTA
        RET
```

`twice` prints `a` two times. The program calls it twice, with different letters in `a`.

## Run it

Open [Run](exuarch:tab/Run) and run it: the LCD shows `OOKK`. Step through the first `CALL` with **Tick** (→) and open the **Memory** tab: the return address appears in the last cell, 4095 (turn to the last page, or click **go to MAR**), and `sp` shows 65535. `RET` reads it back, and `sp` is 0 again. Because every call pushes and every return pops, calls can nest: a subroutine can call another, and each `RET` finds its own return address.

## Forget the push

`RET` finds its way back by reading what `CALL` wrote. What if `CALL` writes nothing?

1. Remove `mem.load` from step 4 of `CALL`: the return address still goes onto the bus, but no cell stores it.
2. Reset and run.

The LCD fills with `O` and the machine never halts: `OO`, then `OO` again, and `K` never comes. The cell `sp` points at was never written, so it still holds 0. Every `RET` reads that 0 into the program counter and returns to the start of the program, which calls `twice` again. `RET` does not know where the program came from; it only knows what `CALL` wrote down.

3. Put `mem.load` back.

Things to try:

- Make `twice` call a subroutine of its own that prints a space after each letter.
- Add `PUSH` and `POP` for `a`, the same way: push is `sp.dec`, then address, then `a.output` `mem.load`.

## Next

- [Reading the keypad](exuarch:guide/reading-the-keypad): let the program react to keys.
- [A stack machine](exuarch:guide/stack-machine): a machine where every value lives on a stack, not only the return addresses.
- [Memory and banks](exuarch:guide/memory-and-banks) covers memory addressing and the MMU's stack bank.
- [Microcode](exuarch:guide/microcode) covers how steps share the bus.
