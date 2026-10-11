# FLIP-16

[GPU-16](exuarch:package/GPU-16) with one change: its screen is **double buffered**. Run GPU-16's spinning cube and you watch every frame happen: the screen goes black, then the faces fill in row by row. Here the same frames are drawn out of sight, and each one appears whole. This is how almost every animated display works, and the trick is called double buffering, or page flipping.

## The idea

[The screen](exuarch:device/fb) is a [`doubleFramebuffer`](exuarch:reference/doubleFramebuffer): it holds two pictures. The **front buffer** is the one on the screen. The **back buffer** is the one that is drawn: every `plot` from the rasterizer, and `clear`, work on it. One control line, `swap`, exchanges the two in a single tick, so the frame that was being drawn is shown, and drawing goes on in the other buffer.

The only new drawing instruction is [SWAP](exuarch:instruction/SWAP), one step with `fb.swap`. It comes after all of GPU-16's instructions, so they have the same opcodes as in GPU-16, and GPU-16's programs run here too: without a `SWAP` they draw, but never show what they drew. After it come three for the frame clock, below.

## How a frame is drawn

The cube's loop is GPU-16's with one line added:

```asm
           CALL   wait          ; the rasterizer has finished the frame
           SWAP                 ; show it
           CLS                  ; clear the new back buffer
           ZCLR
           CALL   build         ; the next frame's triangles
           ...
           GO
```

[CLS](exuarch:instruction/CLS) now clears the back buffer, not the screen. It has to: after a swap, the back buffer still holds the frame before last. A program that drew over only part of it would show pieces of an old frame.

The rule from GPU-16 still holds: wait until the rasterizer is idle before writing the triangle list (see GPU-16's README). It now also determines when to swap. A swap while the rasterizer is still drawing would show a half drawn frame, which is what double buffering is there to prevent.

## Frames on a beat

Some angles of the cube cover more of the screen than others, and the rasterizer takes longer to fill them: at 2 million ticks a second, a frame of [A spinning cube](<exuarch:program/A spinning cube>) takes from about 62 to 77 milliseconds. Shown the moment it is done, the cube turns a little faster at some angles than at others.

[A steady spinning cube](<exuarch:program/A steady spinning cube>) shows its frames on a beat instead. [FRAME CLOCK](exuarch:device/rtc) is a real time clock, an [`rtc`](exuarch:reference/rtc), connected to [BEATS](exuarch:device/pic), an interrupt controller the program only reads, as a status register: interrupts are never switched on. Three instructions use them:

- [TIMI](exuarch:instruction/TIMI) `100` starts the clock beating every 100 milliseconds of real time.
- [TST](exuarch:instruction/TST) puts 1 in A once a beat has come, and 0 until then.
- [TACK](exuarch:instruction/TACK) clears the beat, so `TST` puts 0 in A until the next one.

When a frame is drawn, the program waits for the beat, `TST` until it is not 0, then `TACK` and `SWAP`. Each frame then appears 100 ms after the one before: ten frames a second, the same whatever the angle, as long as the machine draws a frame in less than 100 ms. On a slower computer a frame can miss its beat; it is then shown on the next one, and a longer interval keeps the beat steady.

## Lines from triangles

[A spinning wireframe cube](<exuarch:program/A spinning wireframe cube>) draws the cube's 12 edges instead of its 6 faces, each in a colour of its own. The rasterizer only fills triangles, and drops one with no area, so a line is drawn as a thin quad: two triangles along it, 2 pixels wide. The program widens a line that runs more across than down downwards, and one that runs more down than across sideways, so every edge looks as thick. Each end keeps its own depth, so where two edges cross, the nearer one is in front. With so few pixels to fill, a frame takes about 32,000 ticks, a fifth of the solid cube.

## What real hardware adds

Here a swap happens at once. A real screen is drawn by scanning it top to bottom many times a second. Swap halfway through a scan and the top of the screen shows one frame and the bottom the next, a line called **tearing**. Real displays therefore wait to swap until the scan reaches the bottom, the vertical blank, and that wait is what *vsync* means in game settings. FLIP-16 has no scan, so it cannot tear, but the steady cube's frame clock is the same idea: a beat from outside the program that signals when a new frame may be shown.

## Things to try

1. Step [A triangle drawn out of sight](<exuarch:program/A triangle drawn out of sight>). The trace shows the rasterizer plotting every pixel, the screen stays black, and then `SWAP` shows the whole triangle in one tick. The screen's header shows which buffer is on the screen.
2. Run [A spinning cube](<exuarch:program/A spinning cube>) at **⚡ Max**, then the same program in GPU-16, and compare.
3. Take the `CLS` out of the cube. The new frame is drawn over the one from two frames ago, and the old faces show through where the cube has moved away.
4. Move the `SWAP` to straight after `GO`, so it swaps while the rasterizer is drawing. Work out what each buffer holds after the swap, then run it and check.
5. Run [A steady spinning cube](<exuarch:program/A steady spinning cube>) at **⚡ Max**, then change `TIMI 100` to `TIMI 50` and to `TIMI 250`, and watch the clock speed panel: the machine runs as fast as ever, but spends the rest of each beat waiting in `TST`.
6. Give the wireframe cube thicker lines, or draw only the edges that face the camera.

## Read more

- [Bus masters and coprocessors](exuarch:guide/bus-masters): the framebuffer, the double buffered one, and sharing a bus with the rasterizer.
- [The graphics pipeline](exuarch:guide/graphics-pipeline): triangles, shading, depth and projection.
