# GPU-16

A CPU with a small **3D graphics pipeline**, built the way the first 3D accelerators were: the CPU does the geometry, and a separate chip fills triangles. Every stage is visible in the Run view, one bus transfer per tick.

## The idea

Drawing a 3D scene has two halves. **Geometry** turns the corners of each shape in space and projects them onto the screen: a few multiplications and a division per corner. **Rasterizing** fills every triangle pixel by pixel: cheap work, but a great deal of it. GPU-16 has separate hardware for each half. The machine adds a [multiply-accumulate unit](exuarch:device/mac) to the CPU for the geometry, and [the rasterizer](exuarch:device/rast) fills the triangles by itself on buses of its own, while the CPU runs its own program.

## The parts

- On the **main** bus: an accumulator CPU with [A](exuarch:device/a), [B](exuarch:device/b), [the ALU](exuarch:device/alu), [memory](exuarch:device/mem), [a stack pointer](exuarch:device/sp) and [the LCD](exuarch:device/lcd), plus [the multiply-accumulate unit](exuarch:device/mac), which works in 8.8 fixed point: 256 is 1.0.
- On the **list** bus: [the triangle list](exuarch:device/lmem), a memory the rasterizer reads triangles from. The CPU reaches it through [the bridge](exuarch:device/bridge).
- On the **video** bus: [the screen](exuarch:device/fb) and [the depth buffer](exuarch:device/zb), which holds how near the nearest thing drawn at each pixel is.
- [The rasterizer](exuarch:device/rast) sits on all three: it reads its parameters on main, reads triangles on list and draws on video.

## How a frame is drawn

A triangle is 12 words in the list: x, y, depth and an RGB565 colour for each corner. The CPU writes them with [STL](exuarch:instruction/STL), which crosses the bridge in six ticks per word, fetch included. Then it sets where the triangles start in the rasterizer with [GADR](exuarch:instruction/GADR) and how many there are with [GCNT](exuarch:instruction/GCNT), and starts it with [GO](exuarch:instruction/GO).

The rasterizer reads each triangle, two ticks per word, then fills it row by row. For every pixel it reads the depth buffer, and only if the triangle is nearer there does it write the new depth and plot the colour, blended from the three corner colours. [GST](exuarch:instruction/GST) puts 1 in A while it is still busy.

## Sharing the triangle list

The list bus has two masters. The CPU drives it through the bridge: two of [STL](exuarch:instruction/STL)'s four steps put a value on it, first the address and then the word. The rasterizer drives it too, whenever it reads a triangle. Nothing arbitrates between them: there is no hardware that makes one wait for the other. If both drive the list bus in the same tick, the machine stops with *multiple bus devices has output enabled at the same time on bus 'list'*, the simulator's version of a short circuit on a real bus.

So the rule is kept in software: **do not write the list while the rasterizer is busy.** Before writing a new frame's triangles, wait for it to finish, as [A spinning cube](<exuarch:program/A spinning cube>) does:

```asm
wait:      GST              ; A = 1 while the rasterizer is busy
           CMPI   0         ; GST leaves the flags alone, so compare
           JNE    wait
```

The clash does not happen every time, which makes it easy to miss. The rasterizer only uses the list bus while it reads a triangle's 12 words; while it fills the triangle, the list bus is free and an `STL` goes through. Whether a program without the wait fails depends on timing. Even an `STL` that goes through is wrong: the rasterizer reads `GADR` and `GCNT` again for every triangle and reads each triangle only when it reaches it, so changing the list mid-job changes what it draws.

Real machines solve this in hardware: an arbiter holds the CPU in a wait state until the bus is free, or the memory has two ports. GPU-16 leaves it to the program on purpose, so the rule is visible: remove the wait loop from the cube and run it to see the clash.

## Turning and projecting

For 3D, the CPU first turns each corner with [MA](exuarch:instruction/MA), [MB](exuarch:instruction/MB), [MUL](exuarch:instruction/MUL) and [MAC](exuarch:instruction/MAC): x times the cosine plus z times the sine. It then projects the result with [MDIV](exuarch:instruction/MDIV): x times the focal length, divided by the distance. [MRD](exuarch:instruction/MRD) reads the result back.

## Things to try

1. [One flat triangle](<exuarch:program/One flat triangle>): step it and watch the list bus carry the 12 words, then the video bus fill the rows.
2. [A shaded triangle](<exuarch:program/A shaded triangle>): three corner colours blended across it.
3. [Two triangles through each other](<exuarch:program/Two triangles through each other>): each is nearer on one side, so the depth buffer cuts them along a line neither triangle has. Show the depth buffer next to the screen in the Run view.
4. [A pinwheel from one list](<exuarch:program/A pinwheel from one list>): sixteen triangles and one `GO`, and the CPU prints dots on the LCD all the while.
5. [A spinning cube](<exuarch:program/A spinning cube>): the whole pipeline. Run it at full speed with **⚡ Max**; while the rasterizer draws one frame, the CPU is already turning the corners for the next. A frame is about 150,000 ticks, almost all of them pixels: under a tenth of a second at full speed in a desktop browser. Turn the speed down to watch each frame being drawn.
6. Make the cube faster with **back-face culling**. A face turned away from the camera has its corners in the opposite turning order on the screen to a face turned towards it, so it can be left out of the list. Two multiplications per triangle on the CPU save the rasterizer every pixel of the back faces.

## Read more

- [The graphics pipeline](exuarch:guide/graphics-pipeline): triangles, shading, depth and projection, and what they cost.
- [Bus masters and coprocessors](exuarch:guide/bus-masters): how a device drives a bus by itself.
- [Bridges between buses](exuarch:guide/bridges): how the CPU reaches the triangle list.
