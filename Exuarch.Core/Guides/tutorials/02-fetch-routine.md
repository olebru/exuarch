# Fetch

Watch this tutorial as a video: [episode 2 of Build a computer in ExµArch](https://www.youtube.com/watch?v=EeOZ5pA2SDQ) on YouTube.

The machine from [Ground zero](exuarch:guide/ground-zero) has an empty ROM. In this tutorial you write the first two words: the fetch routine, which reads the next opcode from memory into the counter. Every program depends on it.

Skipped a tutorial, or lost your machine? Load [Tutorial 2 · Fetch](<exuarch:package/Tutorial 2 · Fetch>), under **Tutorial** in **Machines**: it is the machine this tutorial starts from.

## 1. What a tick is

A tick runs from one clock edge to the next. On a breadboard:

1. On the edge, `ir` takes its new value.
2. The ROM puts the word at that address on its data pins, and the control lines settle.
3. The one device with output enable on drives the bus.
4. On the next edge, every device with load on captures the bus, and `ir` counts, loads or clears.

ExµArch does the same in two halves: devices drive their buses, then devices latch. A value only reaches a register at the end of the tick. See [Buses and ticks](exuarch:guide/buses-and-ticks).

## 2. Step 1: send out the address

The program is in `mem` from address 0, and `pc` says which cell is next. First the memory has to know which cell to read: it always reads the cell its own address register, the MAR, points at.

1. Open [Microcode](exuarch:tab/Microcode) and select `FETCH`.
2. Add to step 1: `pc.output` `mem.loadmar`.

`pc` drives its value onto the bus. At the end of the tick, the MAR captures it.

## 3. Try it in one step

Why not read the cell in the same tick?

1. Add `mem.output` `ir.load` `pc.inc` to step 1 as well.

The editor reports `pc.output and mem.output all drive bus 'main'` in the same tick. One bus carries one value per tick, so the address and the data can not both be on it. On real chips that is two outputs fighting over the same wires.

Even with a second bus it would not work: the MAR only changes at the end of the tick, so `mem.output` would read the old address.

2. Remove the three signals again.

## 4. Step 2: load the opcode

1. Click **＋ Add step** and give step 2: `mem.output` `ir.load` `pc.inc`.

The memory drives the cell at the address it latched in step 1. `ir`'s load pin is on, so at the end of the tick it takes that value instead of counting. In the same edge `pc` moves on to the next cell.

The value loaded is the **opcode**. In the next tick the decoder reads the ROM at that address. Loading the counter is a jump inside the ROM.

## 5. Run it on empty memory

The program is empty, so every cell of memory is 0.

1. Open [Run](exuarch:tab/Run) and the **Trace** tab.
2. Press **Tick** six times.

The rows alternate `FETCH.1`, `FETCH.2`. Each `FETCH.2` shows `mem → main 0000 → ir`: the memory drives 0 and `ir` loads it.

3. Open the **Memory** tab and press **Tick** a few more times.

The cell marked **PC** moves on by one every second tick: the machine is reading its memory, cell by cell.

## 6. Why it loops

0 is the address of fetch itself. So after each fetch the decoder starts fetch again, while `pc` counts 1, 2, 3.

The machine walks through memory, two ticks per cell, and does nothing. A cell holding 0 is an instruction that does nothing at all.

## 7. Forget pc.inc

What is `pc.inc` in step 2 for?

1. Press **⟲ Reset**, remove `pc.inc` from step 2, and press **Tick** six times.

`pc` stays at 0, so every fetch reads cell 0 again. Without `pc.inc` the machine would never get past the first cell of its program.

2. Put `pc.inc` back in step 2.

## Next

- [Opcodes are addresses](exuarch:guide/opcodes-are-addresses): write the first real instruction.
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register) goes further into fetch.
