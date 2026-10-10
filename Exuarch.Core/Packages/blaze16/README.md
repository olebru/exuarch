# BLAZE-16

[DSP-16](exuarch:package/DSP-16) rebuilt the way real digital signal processors are built. It draws the same Mandelbrot pictures, pixel for pixel, about eight times faster. That is fast enough to work out the whole set once and then cycle its colours, frame after frame.

| Program | DSP-16 | BLAZE-16 | Faster by |
|---|---|---|---|
| [Mandelbrot, 80 x 60](<exuarch:program/Mandelbrot, 80 x 60>) | 8,239,344 ticks | 1,005,724 ticks | 8.2 times |
| [Mandelbrot, 160 x 120](<exuarch:program/Mandelbrot, 160 x 120>) | 25,605,802 ticks | 3,087,443 ticks | 8.3 times |
| [Mandelbrot, 640 x 480](<exuarch:program/Mandelbrot, 640 x 480>) | 353,232,756 ticks | 44,058,082 ticks | 8.0 times |
| [Lightning at the top of the set](<exuarch:program/Lightning at the top of the set>) | 583,757,144 ticks | 76,403,166 ticks | 7.6 times |

[Colour cycling, 80 x 60](<exuarch:program/Colour cycling, 80 x 60>) works the set out in about 690,000 ticks, then turns its colours by one step every 96,027 ticks.

## Three ideas from real DSPs

**A bus for the program.** The program lives in its own memory, [PROGRAM](exuarch:device/pmem), on its own bus, `ibus`, with the program counter, the instruction register and [WORD](exuarch:device/iw). The data path has the other bus, `dbus`. Because the two buses carry a transfer each in the same tick, every instruction fetches the next one in its own last two ticks, while it still works on `dbus`. There is no separate fetch: FETCH only runs once, after a reset. Most DSPs are built this way, with separate program and data memory: a Harvard architecture, like [HARVARD-16](exuarch:package/HARVARD-16)'s.

**Register numbers in the word.** The top 6 bits of an instruction are its opcode, and the register numbers sit in fields below it, the way [FIELD-16](exuarch:package/FIELD-16) does it. WORD loads the word from `ibus`, and its field buffers drive `dbus`, where the registers are. So `ADD R0, R1, R2` is one word, and choosing a register is one tick. A branch address is a 10 bit field too, which crosses to `ibus` over [BRIDGE](exuarch:device/opr). Program memory has 1024 cells, so 10 bits reach all of it.

**A data path for the job.** DSPs have registers and units for the sums they do most. Here the sum is z = z² + c, so BLAZE-16 has registers for it, [ZX](exuarch:device/zx), [ZY](exuarch:device/zy), [CX](exuarch:device/cx) and [CY](exuarch:device/cy). It also has three multipliers that work at once: [X SQUARED](exuarch:device/sx), [Y SQUARED](exuarch:device/sy) and [X TIMES Y](exuarch:device/xy). One value on the bus can load several of them in the same tick, so x goes into two at once and y into two more.

## MITER: the whole loop in one instruction

[MITER](exuarch:instruction/MITER) runs z = z² + c until z escapes or the steps run out. One step takes 17 ticks:

1. ZX goes into X SQUARED and X TIMES Y, ZY into Y SQUARED and X TIMES Y, and all three multiply.
2. The ALU works out x² - y² and x² + y², and compares x² + y² with [LIMIT](exuarch:device/lim).
3. If it has passed 4, the steps [N](exuarch:device/n) had left go into [LEFT](exuarch:device/k), and N becomes 0. Otherwise N counts down. Either way ZX becomes x² - y² + cx and ZY becomes 2xy + cy.
4. While N is not 0, the last tick puts WORD back on `ibus` into the instruction register. The mapping ROM sends it to MITER's first step again: the same instruction, fetched again from its own latch, with no trip to memory. When N is 0, the last tick fetches the next instruction instead.

The instruction only ever ends at its last step, so a flag the ALU changes on the way can never send it the wrong way: the escape is decided once, and N carries it to the end. LEFT is 0 for a point in the set, and the steps left for one that escaped.

On DSP-16 the same step is twelve instructions and over 100 ticks. The arithmetic is the same to the last bit, which is why the pictures match. 2xy is x times y added to itself, as DSP-16 does it, not a multiplier wired one bit over.

Around it, [MPOINT](exuarch:instruction/MPOINT) starts a point. [MPUT](exuarch:instruction/MPUT) looks the colour for LEFT up in the [COLOURS](exuarch:device/colour) ROM and stores it in [LINE](exuarch:device/line). [MPLOT](exuarch:instruction/MPLOT) puts it straight on the screen instead. Both then step CX to the next point. LINE's address register moves on by itself with `incmar`, so storing a row of colours needs no index at all. [PLOT8](exuarch:instruction/PLOT8) and [PLOT4](exuarch:instruction/PLOT4) draw a colour as a block, one pixel a tick, the fastest the screen takes them.

## Colour cycling

The animation works the set out once. For every point [MSLOT](exuarch:instruction/MSLOT) keeps where its colour sits in the palette, from 1 to 32, or 64 for a point in the set, in [DATA](exuarch:device/dmem). Every frame, [CYC](exuarch:instruction/CYC) adds [PHASE](exuarch:device/ph) to each slot and looks the colour up in the [CYCLE](exuarch:device/cycle) ROM. That ROM holds the palette twice over, so any slot plus any phase lands on a colour, then 32 blacks, so the set itself stays black. Each frame draws the picture as 4 by 4 blocks in a 320 by 240 window in the middle of the screen.

The screen is the limit: it takes one pixel a tick, so a frame of the whole screen is 307,200 ticks, however fast the CPU is. The window is a quarter of that. Of the 96,027 ticks a frame, 76,800 are pixels, 14,400 are colour lookups, and the rest is the loops.

## On a breadboard

Every part is a chip you can buy and wire:

- **The two buses** are two sets of 16 wires. PROGRAM, the program counter and the instruction register sit on one, the registers, ALU and multipliers on the other.
- **WORD** is two 74HC574 latches clocked from `ibus`. Each field is a 74HC244 buffer from the latch outputs to `dbus`, enabled by its field line.
- **The mapping ROM** is an EEPROM between the opcode bits and the step counter's load inputs, as in [Opcode fields](exuarch:guide/opcode-fields).
- **The ROMs** are EEPROMs burnt with the tables shown in their contents. **The multipliers** are a real simplification: a 16 by 16 multiplier that answers in one tick is a big chip, or a slow circuit. [ExµArch and real hardware](exuarch:guide/real-hardware) lists that one.

## Things to try

1. Run [Mandelbrot, 80 x 60](<exuarch:program/Mandelbrot, 80 x 60>) here and on DSP-16 with **Max** speed, and compare the cycle counts when they halt.
2. Tick through one MITER with **Instruction** and watch WORD go back into the instruction register on its last tick.
3. Open the [CYCLE](exuarch:device/cycle) ROM's contents in Hardware design and change the palette. The animation picks it up when you run it again.
4. Turn the colours the other way: count R7 down with `SUBI` in the animation, and wrap it from -1 back to 31.
