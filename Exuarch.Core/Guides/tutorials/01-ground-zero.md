# Ground zero

A CPU does nothing on its own. Every device waits for a wire to tell it to act, and the decoder drives those wires. In this tutorial you wipe a machine's microcode down to nothing and watch what is left: a counter, counting.

This is the first of four short tutorials that build the core of a CPU one piece at a time. Keep the machine open; each one carries on from the last.

## 1. Make a machine

1. Click **New…** at the top, pick **Minimal CPU**, give it a name and click **Create**.
2. Open [Hardware design](<exuarch:tab/Hardware design>).

You see five devices and the decoder:

- `pc`, a register used as the program counter.
- `mem`, the memory, with its own address register, the MAR.
- `ir`, the instruction register, which is also the decoder's step counter: it picks which word of the decoder ROM is used in each tick. Step 2 below says why one register does both jobs.
- `status`, the flags. Nothing changes it in these tutorials.
- `clk`, the clock.

Between them they have 17 control lines. Click a device to see its lines in the inspector.

## 2. Picture the chips

ExµArch behaves like real chips on a breadboard, so picture them:

- A **register** has two control pins. *Output enable* connects it to the bus wires. *Load* makes it capture the bus on the next clock edge. `pc.output` and `pc.load` are those pins.
- The **decoder** is a ROM, a lookup table in a chip. Put a number on its address pins and the word stored there appears on its data pins. Each data pin is wired to one control pin in the machine, so one word is one bit for each of the 17 lines. That word is a **micro step**.
- **`ir`** is a counter chip, wired to the ROM's address pins. On every clock edge it either clears to 0 (`ir.reset`), loads the bus (`ir.load`), or counts up by one. If both lines are on, clearing wins.

The loop to remember: the counter picks a ROM word, and that word sets the counter's own pins for the next edge.

Why is a counter called the instruction register? Many breadboard CPUs have two chips here: an instruction register that holds the opcode, and a small step counter, and the ROM address is made from both. ExµArch uses one. Every instruction's steps sit in the ROM back to back, and its opcode is the address of its first step. So when a program's opcode is loaded into `ir`, `ir` points at the start of that instruction, and from there it counts through its steps. One number says both which instruction is running and which step of it: where in the ROM the machine is. [Opcodes are addresses](exuarch:guide/opcodes-are-addresses) shows it at work.

## 3. Clear the microcode

1. Open [Microcode](exuarch:tab/Microcode).
2. Select `NOP` and click **Delete**. Do the same with `JMP` and `HLT`.
3. Select `FETCH`. Delete its step 2 with ✕.
4. Remove both signals from step 1 with their ×.

The ROM now holds one word, at address 0, with every bit off. The meter in the toolbar reads 1 / 65536: one of the ROM's addresses is in use.

## 4. Clear the program

The program uses `NOP` and `HLT`, which are gone.

1. Open [Program](exuarch:tab/Program) and delete the lines `NOP` and `HLT`. The comment can stay.

## 5. Watch the counter count

1. Open [Run](exuarch:tab/Run), and below the machine open the **Decoder ROM** tab.
2. Press **Tick** (→) five times and watch the bits of the micro step register.

`ir` counts 1, 2, 3, 4, 5. No signal asked it to: clear and load are both off, so on each clock edge the counter does the only other thing it can, count up.

## 6. Read the trace

1. Open the **Trace** tab.

Each row is one tick, newest on top. Every row shows `ir 0000→0001`, `ir 0001→0002` and so on, and nothing else changes. Only the oldest row has a step name, `FETCH.1`: the other addresses hold nothing, so every line is off.

The decoder does not know what an instruction is, or that memory exists. Every tick it reads one word and turns on the lines that word says. Right now every word is empty.

2. Press **⟲ Reset** to put `ir` back to 0.

## 7. Switch one line on

Give the one word something to do.

1. In [Microcode](exuarch:tab/Microcode), add `pc.inc` to step 1 of `FETCH`.
2. In [Run](exuarch:tab/Run), press **Tick** five times.

`pc` goes to 1 on the first tick and then stays there. The word is at address 0, and after one tick the counter has moved on to address 1, which is empty. A line is only on while the counter points at its word.

3. Press **⟲ Reset**, and add `ir.reset` to the same step.
4. Press **Tick** ten times.

Now `pc` counts 1, 2, 3 … 10, and `ir` stays at 0. The word says: count `pc` up, and clear the counter. So the counter comes back to address 0 every tick and picks the same word again. That is a loop, made of one word and two bits, and every instruction you build works the same way: the counter picks a word, and the word decides where the counter goes next.

## 8. Try it: every second tick

Make `pc` count up on every second tick instead of every tick. You need a second word: click **＋ Add step** in `FETCH`.

If you get stuck: step 1 `pc.inc`, step 2 `ir.reset`. After ten ticks `pc` is 5.

When you are done, delete step 2 and remove the signals from step 1, so the ROM is back to one empty word for the next tutorial.

## Next

- [Fetch](exuarch:guide/fetch-routine): put the first two words in the ROM, so the machine reads its program.
- [Devices and control lines](exuarch:guide/devices-and-control-lines) covers what each line does.
