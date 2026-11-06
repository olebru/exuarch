# The graphics pipeline

A 3D picture is made of triangles. Each frame, every corner of every triangle is turned and projected onto the screen, and then every triangle is filled pixel by pixel. The first half is a little arithmetic per corner; the second is a small amount of work for a very large number of pixels. That imbalance is why graphics hardware exists, and [GPU-16](exuarch:package/GPU-16) builds the pipeline out of three devices you can watch working: a [rasterizer](exuarch:reference/rasterizer), a [depth buffer](exuarch:reference/depthBuffer) and a [multiply-accumulate unit](exuarch:reference/mac).

## Filling a triangle

The rasterizer fills a triangle by testing pixel centres. For each edge it computes on which side a pixel's centre lies, and a pixel is inside when its centre is on the inside of all three edges. It does this a row at a time and draws the span of pixels between the first and the last one inside.

A pixel whose centre lies exactly on an edge needs a rule, or two triangles sharing that edge would both draw it, or neither would. The rasterizer uses the **top-left rule**: such a pixel belongs to the triangle for which the edge is a top edge (flat, with the triangle below it) or a left edge. A mesh of triangles therefore has no gaps and no pixel drawn twice. Pixels outside the 640 × 480 screen are left out.

Each row starts with two transfers on the video bus, the column and the row for the framebuffer's cursor, and then one `plot` per pixel, since `plot` moves the cursor on by itself.

## Shading

Every corner has its own colour. For each pixel, the rasterizer weighs the three colours by how near the pixel is to each corner, using the same edge sums it tested with, and mixes red, green and blue separately. This is **Gouraud shading**. When the three colours are equal the triangle is flat; when they differ you get smooth gradients, as on the corners of the RGB cube in the spinning cube demo.

## Hiding what is behind

Each corner also has a depth, where smaller is nearer, and the rasterizer blends it across the triangle like a colour. With a depth buffer connected, each pixel takes a round trip through memory:

1. Read the depth already stored at that pixel (one tick: the depth buffer drives the video bus).
2. If the new pixel is nearer, write its depth and plot its colour (two ticks).
3. If not, move both cursors past it (one tick, with the depth buffer's `next` and the framebuffer's `skip`).

So a visible pixel costs three ticks instead of one, and even a hidden pixel costs two. Triangles can be drawn in any order and still overlap correctly, and they can even cut through each other. The price is memory traffic, which is what real GPUs spend most of their cycles on.

Clear the depth buffer before each frame with its `clear` line: every depth becomes 65535, as far away as it can be.

## Reading triangles by itself

The rasterizer does not take triangles from the CPU one value at a time. They sit in a memory on its **list** bus, 12 words each: x, y, depth and colour for each of the three corners. The CPU writes the address of the first triangle and the count to it, starts it, and from then on the rasterizer reads each word itself. That takes two ticks per word: one to put the address in the memory's MAR, one to read the cell. This is DMA, direct memory access, and it is what lets one `GO` draw a whole mesh. The CPU must not use the list bus while the rasterizer is running, so it writes the next frame's triangles only after the rasterizer's status reads 0.

## Turning and projecting

The geometry is done by the CPU, with extra hardware. The ALU can add but not multiply, so GPU-16 has a multiply-accumulate unit that works in **8.8 fixed point**: a 16 bit number with 8 bits after the point, so 256 means 1.0 and 128 means 0.5. To turn a corner about the vertical axis by an angle, the CPU loads the angle's cosine and sine, times 256, from a table:

```asm
        LDA  vx        ; x' = x * cos + z * sin
        MA
        LDA  cosv
        MB
        MUL
        LDA  vz
        MA
        LDA  sinv
        MB
        MAC
        MRD            ; the accumulator shifted right by 8
```

**Projection** makes far things small: the screen x is the centre plus x times a focal length, divided by the distance. The unit's `div` line divides the accumulator and keeps the fixed point scale, so `MUL` by the focal length followed by `MDIV` by the distance gives the result directly.

## Two processors at once

The CPU and the rasterizer work on different buses, so they can both be active in the same tick. The spinning cube uses that: while the rasterizer fills one frame, the CPU turns the corners for the next. In the Run view you can see the main bus and the video bus carry values in the same ticks.

## See also

- [Bus masters and coprocessors](exuarch:guide/bus-masters)
- [Bridges between buses](exuarch:guide/bridges)
- [The rasterizer](exuarch:reference/rasterizer), [the depth buffer](exuarch:reference/depthBuffer) and [the multiply-accumulate unit](exuarch:reference/mac)
- [GPU-16](exuarch:package/GPU-16)
