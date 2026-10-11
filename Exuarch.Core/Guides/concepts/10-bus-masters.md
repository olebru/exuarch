# Bus masters and coprocessors

In most of a machine, nothing happens unless the microcode says so. Every control line is switched on by a step, and the decoder is the only thing issuing steps. A bus master breaks that rule. It is a device that drives another device's control lines by itself, tick after tick, while the CPU carries on with its own program. ExµArch has two: the blitter, a small graphics coprocessor that fills rectangles, and the [rasterizer](exuarch:reference/rasterizer), which fills triangles in [GPU-16](exuarch:package/GPU-16).

## The framebuffer it draws on

A [`framebuffer`](exuarch:reference/framebuffer) is a 640 × 480 colour screen. Each pixel is one 16 bit RGB565 word: 5 bits of red, 6 of green and 5 of blue, so `0xF800` is red, `0x07E0` green and `0x001F` blue. It has a cursor and five control lines:

- `loadx` sets the cursor column from the bus. The value wraps at 640.
- `loady` sets the cursor row from the bus. The value wraps at 480.
- `plot` writes the bus value at the cursor and moves one pixel right. Past the last column it goes to the start of the next row, and past the last row back to the top.
- `skip` moves the cursor one pixel right the same way, without writing.
- `clear` blanks the screen and puts the cursor at the top left.

A [`doubleFramebuffer`](exuarch:reference/doubleFramebuffer) is the same screen with two pictures, and one more line:

- `swap` shows the picture that was being drawn, and from then on drawing goes to the other one.

`plot`, `skip` and `clear` always work on the back buffer, out of sight, while the front buffer stays on the screen. A program draws a whole frame and then swaps, so the screen never shows a frame half drawn. After a swap the back buffer still holds the frame before last; clear it, or draw over all of it. A swap is instant, and there is no waiting for the screen's refresh as on real hardware. It can stand in for a framebuffer anywhere, including as the screen of a blitter or a rasterizer.

A CPU can drive these lines from its own microcode, as BYOC-16 does. Filling a rectangle that way costs at least one tick per pixel, and the CPU can do nothing else meanwhile.

## The blitter

A [`blitter`](exuarch:reference/blitter) has two bus ports, `host` and `video`, and one connection, `screen`, which must name a framebuffer. Put the host port on the CPU's bus, and put the video port and the framebuffer on a bus of their own. Its control lines all face the host side:

- `loadx`, `loady`, `loadw` and `loadh` take the rectangle's left column, top row, width and height from the host bus.
- `loadcolour` takes the RGB565 fill colour from the host bus.
- `start` starts the job, if the width and height are both more than 0.
- `status` puts 1 on the host bus while the blitter is busy and 0 when it is idle.

After `start`, the blitter works on its own. Every tick, in the drive half, it puts one value on the video bus and switches on one of the screen's lines, just as microcode would:

1. the rectangle's column, with `loadx`;
2. the current row, with `loady`;
3. the colour, with `plot`, once for each pixel across the width.

Then it moves to the next row. A row takes its width plus two ticks, so a 200 × 150 rectangle takes 150 × 202 ticks. There is no clipping: a rectangle that runs past the right edge wraps, as the cursor does.

The blitter reads its rectangle and colour as it goes, so wait until `status` reads 0 before you load the next job.

## Two buses, two things at once

The point of the separate video bus is that the blitter's transfers never meet the CPU's. Each bus carries one value per tick, and a second device driving it in the same tick stops the machine with a bus error. With the framebuffer alone on the video bus, the blitter has that bus to itself. The CPU keeps fetching and running instructions on the main bus in the same ticks.

In the Run view you can see both. The **Last tick** panel lists each bus with the device that drove it and the devices that read it. On the video bus the driver is the blitter and the reader is the screen, even though no microcode step named them.

## Working alongside a bus master

Once a bus master has started, two things run at once, and neither waits for the other. Three rules follow, and they hold for the blitter and the rasterizer alike.

**A start while busy is ignored.** Both devices drop a `start` that arrives while they are still working, without any error. Read `status` first, and only start a new job when it reads 0.

**Its settings are read live.** The blitter reads its rectangle and colour again on every row and pixel, and the rasterizer reads its list address and count again for every triangle, and each triangle only when it gets to it. Nothing is copied when the job starts, so changing a setting, or the triangle list, in the middle of a job changes the job. Wait for `status` to read 0 before setting up the next one.

**A shared bus needs a rule.** If the CPU can reach a bus that the bus master also drives, the two can drive it in the same tick, and the machine stops with a bus error, the simulator's version of a short circuit. The blitter avoids this by design: in COPRO-16 and IRQ-16 its video bus holds only the screen, and the CPU has no way onto it. The rasterizer cannot avoid it: in GPU-16 the CPU writes the triangle list over the same list bus the rasterizer reads it from. A write that lands while the rasterizer is reading a triangle shorts the bus, one that lands while it is filling gets through, so a program without the rule can run for a while before it fails. The [GPU-16 README](exuarch:package/GPU-16) shows the clash.

In the built in machines, the program keeps these rules: a software guard. It polls `status` in a loop, or, with an interrupt controller, waits for the interrupt the device raises when it is done:

```asm
wait:   GST             ; GPU-16: A = 1 while the rasterizer is busy
        CMPI   0
        JNE    wait
```

Real machines often keep them in hardware instead. An **arbiter** grants the shared bus to one master at a time and holds the other in a wait state until the bus is free. **Dual-ported memory** gives each master its own port, so they never share a bus. **Shadow registers** let the CPU load the next job's settings while the current one runs, and copy them across on `start`. None of these are built in, so you can see what goes wrong without them, and add one yourself.

## COPRO-16

[COPRO-16](exuarch:package/COPRO-16) is built around this idea. The accumulator CPU lives on `main` with its memory and the LCD. The blitter sits between `main` and `video`, and the framebuffer is on `video`. The instruction set gives the CPU one instruction per blitter line:

```asm
        BXI    80          ; left column
        BYI    80          ; top row
        BWI    200         ; width
        BHI    150         ; height
        BCI    0xF800      ; red
        GO                 ; blit.start: the blitter takes over
wait:   BST                ; A = 1 while busy
        ...
```

`BXI` through `BCI` read their operand from program memory and switch on `blit.loadx` through `blit.loadcolour` with it on the bus. `GO` is a single step with `blit.start`. `BST` puts `blit.status` into A. Between `GO` and the moment `BST` reads 0, the program is free to do other work, such as printing on the LCD.

## Finishing with an interrupt

Polling `status` works, but the blitter can also signal the CPU when it is done. It raises an interrupt request each time a job finishes. Connect it to one of an interrupt controller's sources, and the CPU is interrupted instead of having to poll. [IRQ-16](exuarch:package/IRQ-16) wires it to `irq2` this way. See [interrupts](exuarch:guide/interrupts).

## See also

- [Bridges between buses](exuarch:guide/bridges)
- [Interrupts](exuarch:guide/interrupts)
- [blitter reference](exuarch:reference/blitter) and [framebuffer reference](exuarch:reference/framebuffer)
- [COPRO-16](exuarch:package/COPRO-16)
