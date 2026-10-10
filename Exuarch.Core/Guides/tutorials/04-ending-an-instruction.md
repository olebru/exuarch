# Ending an instruction

Watch this tutorial as a video: [episode 4 of Build a computer in ExµArch](https://www.youtube.com/watch?v=j8EajwHeD7A) on YouTube.

`HLT` from [Opcodes are addresses](exuarch:guide/opcodes-are-addresses) ends by stopping the clock. Every other instruction has to end by sending the counter back to fetch. In this tutorial you write `NOP` and `JMP`, and see what happens when the counter is not sent anywhere.

Skipped a tutorial, or lost your machine? Load [Tutorial 4 · Ending an instruction](<exuarch:package/Tutorial 4 · Ending an instruction>): it is the machine this tutorial starts from.

## 1. Write NOP

1. In [Microcode](exuarch:tab/Microcode), click **＋ Instruction** and set the mnemonic to `NOP`.
2. Keep its one step, `ir.reset`.

`NOP` is opcode 3, after `HLT`. `ir.reset` is the counter's clear pin: at the end of the tick `ir` becomes 0, and the next tick reads the first step of fetch.

## 2. Run two NOPs

1. Change the program to:

```asm
        NOP
        NOP
        HLT
```

2. Run it. Memory holds `0003 0003 0002`.

It takes 9 ticks: fetch and `NOP` twice, then fetch and `HLT`.

## 3. Forget ir.load

What makes the machine follow the program?

1. Remove `ir.load` from fetch's step 2.

The editor warns that `FETCH` never resets or loads `ir`, so it runs on into the next instruction's microcode.

2. Run the program again.

It halts after 3 ticks, and the `NOP`s never run. Without the load, `ir` counts 0, 1, 2, and address 2 is `HLT`, whatever memory says. The opcodes in memory only matter because `ir.load` puts them into the counter.

3. Put `ir.load` back.

## 4. Write JMP

`JMP` reads the cell after its opcode and makes it the program counter.

1. Click **＋ Instruction**, set the mnemonic to `JMP`, **Operands** to 1 and the operand type to **address**.
2. Give it two steps:
   - step 1: `pc.output` `mem.loadmar`
   - step 2: `mem.output` `pc.load` `ir.reset`

`JMP` is opcode 4. It is fetch with one change: the value from memory goes into `pc` (`pc.load`) instead of `ir`. Fetch has already moved `pc` past the opcode, so step 1 sends out the operand's address.

## 5. Loop for ever

1. Change the program to:

```asm
loop:   NOP
        JMP loop
```

`loop:` is a label for address 0. Memory holds `0003 0004 0000`.

2. Run it with **Space**, and pause with **Space**.

It never halts. `NOP` takes three ticks and `JMP` four, and then `pc` is back at 0.

## 6. The whole ROM

1. Check **whole ROM** in the **Decoder ROM** tab.

Each row is one address and each column one control line:

| Address | Step | Lines on |
|---|---|---|
| `00000` | `FETCH.1` | `pc.output` `mem.loadmar` |
| `00001` | `FETCH.2` | `mem.output` `ir.load` `pc.inc` |
| `00002` | `HLT.1` | `clk.disable` |
| `00003` | `NOP.1` | `ir.reset` |
| `00004` | `JMP.1` | `pc.output` `mem.loadmar` |
| `00005` | `JMP.2` | `mem.output` `pc.load` `ir.reset` |

Every program this machine runs is these six words, picked in the order the counter and memory decide.

## 7. Forget ir.reset

What happens to an instruction that does not send the counter back?

1. Remove `ir.reset` from `NOP`'s step. The editor warns that `NOP` never resets or loads `ir`.
2. Reset, open the **Trace** tab and press **Tick** five times.

The rows read `FETCH.1`, `FETCH.2`, `NOP.1`, then `JMP.1` and `JMP.2`. Nothing clears the counter after `NOP.1`, so it counts on to address 4, and `NOP` runs straight on into `JMP`'s steps. `JMP` takes the next cell as its address, which is `JMP`'s own opcode, 4, so the machine jumps to cell 4 and walks off through empty memory for ever.

The steps of an instruction do not end on their own. Each one ends only because its last step says where the counter goes next.

3. Put `ir.reset` back in `NOP`.

## Next

- [Your first machine](exuarch:guide/first-machine): add a display and an instruction that prints.
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register) covers instructions that fetch their successor themselves.
