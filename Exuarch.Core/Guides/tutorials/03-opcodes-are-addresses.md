# Opcodes are addresses

The machine from [Fetch](exuarch:guide/fetch-routine) can read opcodes, but the only one it knows is 0, fetch itself. In this tutorial you add `HLT` and see what an opcode really is: the ROM address where an instruction's steps begin.

## 1. Where instructions go in the ROM

Fetch starts at address 0. Each instruction you add gets as many addresses as it has steps, right after the one before. Fetch has two steps, at 0 and 1, so the next instruction starts at 2.

## 2. Write HLT

1. In [Microcode](exuarch:tab/Microcode), click **＋ Instruction** and set **Mnemonic** to `HLT`.
2. The new instruction has one step holding `ir.reset`. Remove it and add `clk.disable`.

`HLT` never goes back to fetch: it stops the clock, so it does not need `ir.reset`. [Ending an instruction](exuarch:guide/ending-an-instruction) is about the instructions that do.

Look at the number next to `HLT` in the list: `HLT` is opcode 2, the next free address. Opcodes follow from the order and length of the instructions, so the same instruction can have another number in another machine: in the minimal CPU that **New…** makes, `NOP` and `JMP` come first and HLT is opcode 5.

## 3. Write the program

1. Open [Program](exuarch:tab/Program) and write:

```asm
        HLT
```

2. Open [Run](exuarch:tab/Run) and the **Memory** tab.

Cell 0 holds `0002`. The assembler turned the mnemonic into its opcode, and that number is all the program is.

## 4. Step through it

1. Open the **Trace** tab and press **Tick** until the machine halts. It takes 3 ticks:

- `FETCH.1`: `pc → main 0000 → mem`. The address goes out.
- `FETCH.2`: `mem → main 0002 → ir`. The opcode goes into the counter.
- `HLT.1`: `clk.disable` stops the clock. **Tick** does nothing until you reset.

## 5. Watch the ROM address

1. Press **⟲ Reset** and open the **Decoder ROM** tab.
2. Tick through again.

The highlighted row moves `FETCH.1`, `FETCH.2`, `HLT.1`, and the address bits show 0, 1, 2.

## 6. The whole address

The address has more bits than the counter:

```
bit   20 19 18 17 16 | 15 14 13 ...  2  1  0
       I  N  V  C  Z |  the 16 bits of ir
```

The low 16 bits come from `ir`. The top 5 are the flags in `status` and the interrupt request. Here they are all 0, so the address is just `ir`.

When an ALU sets the Z flag later on, step 3 is read from `10003` instead of `00003`: a separate copy of the ROM for that flag. That is how a conditional jump picks its steps, in [Loops and flags](exuarch:guide/loops-and-flags).

## 7. Any number is an opcode

The machine does not know which numbers are instructions. Whatever fetch loads, the decoder jumps to.

1. Change the program to a plain number, 1:

```asm
        .DATA 1
```

2. Reset, open the **Trace** tab and press **Tick** six times.

1 is the address of `FETCH.2`, so after the first fetch the machine jumps into the middle of fetch. The rows read `FETCH.1`, then `FETCH.2`, `FETCH.2`, `FETCH.2` … for ever. Nothing loads the MAR again, so every `FETCH.2` reads cell 0, finds 1 and jumps back to itself, while `pc` counts on. The machine never halts.

3. Put `HLT` back as the program.

## Next

- [Ending an instruction](exuarch:guide/ending-an-instruction): send the counter back to fetch, and jump.
- [Microcode](exuarch:guide/microcode) covers opcodes and the editor in depth.
