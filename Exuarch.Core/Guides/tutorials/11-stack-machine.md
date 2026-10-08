# A stack machine

So far every instruction has said where its values are: `ADD` worked on `a` and `b`, `LAI` loaded `a`. A stack machine never says. Its values sit on a stack in memory, and every instruction takes what it needs from the top and leaves its result there. In this tutorial you build one from the minimal CPU, with three instructions, `PUSH`, `ADD` and `OUT`, and use them to work out `2 + 3 + 4`.

This tutorial starts from scratch, so it does not need the machine from the others.

## 1. Start from the minimal CPU

1. Click **New…**, pick **Minimal CPU**, give it a name and click **Create**.

It has a program counter `pc`, a memory `mem` of 4096 cells, the instruction register `ir`, a status register `status` and a clock `clk`, with `NOP`, `JMP` and `HLT`.

## 2. Add the stack pointer

1. Open [Hardware design](<exuarch:tab/Hardware design>) and drag a **register** onto the bus.
2. Set its **ID** to `sp`.

`sp`, the stack pointer, holds the address of the value on top of the stack. The stack lives in `mem` with the program, so it grows down from the top of memory while the program sits at the bottom. A push first counts `sp` down with `sp.dec` and then writes at the new address; a pop reads and then counts it up with `sp.inc`.

`sp` starts at 0 like every register, so the first push counts it down to 65535. The memory takes an address modulo its size, so 65535 is its last cell, 4095. How much of this a real machine does too is in [Memory and banks](exuarch:guide/memory-and-banks), under *Why a stack needs no setup*.

## 3. Add the ALU and its two inputs

1. Drag two more **register** devices onto the bus and set their IDs to `x` and `y`.
2. Drag an **alu** onto the bus. Under **Connections** set **a** to `x`, **b** to `y` and **status** to `status`.
3. Drag a **display** onto the bus and set its ID to `lcd`.

The ALU only reads registers, and the values are in memory, so `x` and `y` are where they wait while it adds them.

## 4. Write PUSH

Open [Microcode](exuarch:tab/Microcode), click **＋ Instruction**, set the mnemonic to `PUSH`, **Operands** to 1 and the operand type to **value**, and give it four steps:

- step 1: `pc.output` `mem.loadmar` `sp.dec`
- step 2: `mem.output` `x.load` `pc.inc`
- step 3: `sp.output` `mem.loadmar`
- step 4: `x.output` `mem.load` `ir.reset`

Steps 1 and 2 read the operand into `x`, the way `OUT` read its operand in [Your first machine](exuarch:guide/first-machine). Step 1 also counts `sp` down, at the end of the tick, so it is ready by step 3. Step 3 puts the new top address in the memory's address register, and step 4 writes `x` there. With fetch, a `PUSH` takes 6 ticks.

## 5. Write ADD

`ADD` has no operands. It pops two values and pushes their sum:

- step 1: `sp.output` `mem.loadmar`
- step 2: `mem.output` `y.load` `sp.inc`
- step 3: `sp.output` `mem.loadmar`
- step 4: `mem.output` `x.load`
- step 5: `alu.add` `mem.load` `ir.reset`

Steps 1 and 2 pop the top value into `y`. Steps 3 and 4 read the value below it into `x`, but leave `sp` pointing at it. Step 5 lets the ALU put `x + y` on the bus and writes it back into that same cell, which is now the top of the stack. Two values went in, one came out, and the stack is one shorter. With fetch it takes 7 ticks.

## 6. Write OUT

`OUT` pops a value and prints it:

- step 1: `sp.output` `mem.loadmar`
- step 2: `mem.output` `lcd.load` `sp.inc` `ir.reset`

## 7. Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
        PUSH 2
        PUSH 3
        ADD
        PUSH 4
        ADD
        PUSH '0'
        ADD
        OUT
        HLT
```

Read it as `2 3 + 4 + '0' + print`: each number is pushed, and each `ADD` folds the two values on top into one. Adding the character code of `'0'` turns the 9 into the character `9`.

## 8. Run it

Open [Run](exuarch:tab/Run), open the **Trace** and **Tick** through the first `PUSH`:

- `PUSH.1`: `pc` addresses the operand, and `sp` goes from `0000` to `FFFF`.
- `PUSH.2`: the memory drives `0002` into `x`.
- `PUSH.3`: `sp` drives `FFFF` into the memory's address register.
- `PUSH.4`: `x` drives `0002` and the memory stores it: `mem[0FFF]=0002`, the last cell.

Then open the **Memory** tab, go to the last page, and run on. This is the stack after each instruction, top last:

| After | `sp` | The stack |
|---|---|---|
| `PUSH 2` | 65535 | 2 |
| `PUSH 3` | 65534 | 2, 3 |
| `ADD` | 65535 | 5 |
| `PUSH 4` | 65534 | 5, 4 |
| `ADD` | 65535 | 9 |
| `PUSH '0'` | 65534 | 9, 48 |
| `ADD` | 65535 | 57 |
| `OUT` | 0 | empty |

The LCD shows `9`, after 52 ticks. A pop does not wipe the cell it leaves: when the program stops, cell 4094 still holds the 48 that `PUSH '0'` put there, and only `sp` says it is no longer on the stack.

Things to try:

- Write `SUB` like `ADD`, with `alu.sub`. It works out the value below minus the top, so `7 2 SUB` leaves 5.
- Write `DUP`, which pushes a copy of the top: read the top into `x` while counting `sp` down, then write `x` at the new top.

## Next

- [STACK-16](exuarch:package/STACK-16) is a whole stack machine, with `DUP`, `SWAP`, `OVER`, jumps that pop the value they test, and `CALL` and `RET` on a return stack of their own.
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack) uses a stack for return addresses only.
