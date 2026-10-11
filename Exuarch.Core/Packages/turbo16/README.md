# TURBO-16

[FLIP-16](exuarch:package/FLIP-16)'s spinning cube, with the hardware, microcode and program rebuilt for one thing: frames per tick. The picture is the same pixel for pixel, at every one of the 64 angles. It takes 24,903 ticks a frame instead of 143,879, so the cube spins about 5.8 times faster at the same clock.

| | FLIP-16 | TURBO-16 |
|---|---|---|
| Ticks per frame, once running | 143,879 | 24,903 |
| First frame on the screen | tick 148,378 | tick 32,821 |
| Triangles drawn per frame | 12 | 2 to 6 |
| Ticks per pixel drawn | 3, plus 2 for each hidden one | 1 |

## Where the time went

In FLIP-16 the [rasterizer](exuarch:device/rast) draws all 12 triangles and tests every pixel against the depth buffer: one tick to read the depth, then two to write the depth and plot, or one to skip a hidden pixel. The faces at the back are drawn too, only to be covered by the ones at the front. The CPU then waits for the rasterizer before it builds the next frame's triangle list, because the two share the list bus.

## What TURBO-16 does instead

**No depth buffer.** A cube is convex: the faces that point at the camera never cover each other, and the faces that point away are always hidden. So if only the front faces are drawn, nothing needs depth testing. Without a depth buffer the rasterizer plots each pixel in one tick.

**Back faces culled, exactly.** A triangle points at the camera when its corners go round the screen one way, and away when they go round the other way. `build` works out the same signed area the rasterizer does, `(x1 - x0)(y2 - y0) - (y1 - y0)(x2 - x0)`, from the same screen coordinates, and keeps the triangle only when it is positive. The products are too big for 16 bits, so the area goes through [xp](exuarch:device/xp), a second multiply-accumulate unit with no fixed point shift, and only its sign is used ([XRD](exuarch:instruction/XRD), then `CMPI 0` and [JMI](exuarch:instruction/JMI)). A triangle on its edge, with an area of 0, is dropped, as the rasterizer would drop it.

**Lap one overlaps the work.** While the rasterizer draws one angle, the CPU turns, projects and culls the next one into ordinary memory with [PUTM](exuarch:instruction/PUTM), where it is out of the rasterizer's way. When the rasterizer is done, [COPYT](exuarch:instruction/COPYT) copies a whole triangle into the triangle list in 39 ticks, three per word, and the block stays there.

**Every later lap is a replay.** The triangle list holds a block for each of the 64 angles, and `starts` and `counts` hold where each begins and how many triangles it has. A frame is one instruction, [WAITGO](exuarch:instruction/WAITGO). It reads the rasterizer's status into the flags. While the rasterizer is busy, it moves the program counter back onto itself and fetches itself again, four ticks a round. When the rasterizer is idle, it swaps, clears, points the rasterizer at the block in [mp](exuarch:device/mp) with the count in [lp](exuarch:device/lp), and starts it, two ticks later. The CPU loads the next block's address and count while the frame is being drawn.

## How close to the limit it is

A frame has 23,699 lit pixels on average, and the rasterizer can plot one a tick, so that is the floor. The other 1,204 ticks are the rasterizer's own: two ticks at the start of every row of every triangle, to place the cursor, and 24 to read each triangle. The CPU's share is the few ticks between `WAITGO` detecting that the rasterizer is done and the next start.

Splitting each face into its two triangles along the other diagonal would make the rows a little cheaper, but the colours are blended across each triangle, so that would change the picture.

## Things to try

1. Run [A spinning cube](<exuarch:program/A spinning cube>) at **⚡ Max**, then FLIP-16's at the same speed, and compare the clock graphs and how fast the cube turns.
2. Change the `JMI` in `build` to `JPL`, so it keeps the faces pointing away instead. You see the far side of the cube, from inside, as if the near faces were glass.
3. Delete the `JMI` line, so all 12 triangles are drawn. With no depth buffer, whichever face is drawn last ends up on top, and the back faces show through the front ones.
4. Tick through a `WAITGO` with the **Trace** open while the rasterizer is drawing: the same instruction is fetched again and again, four ticks a round, until the flags show it is idle.

## Read more

- [FLIP-16](exuarch:package/FLIP-16) and [GPU-16](exuarch:package/GPU-16), where this cube comes from.
- [The graphics pipeline](exuarch:guide/graphics-pipeline): triangles, shading, depth and projection.
- [Bus masters and coprocessors](exuarch:guide/bus-masters): why the CPU has to keep off the list bus while the rasterizer works.
- [Flags and conditions](exuarch:guide/flags-and-conditions): the conditional steps `WAITGO` is made of.
