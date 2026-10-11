# DSP-16

[RISC-16](exuarch:package/RISC-16) with one part more: a hardware multiplier. That is what makes a digital signal processor: filters, audio and graphics are mostly sums of products, so a DSP multiplies and adds in one go where an ordinary CPU needs a loop. Here it is enough to draw the Mandelbrot set in colour, and to close in on it.

## The multiplier

The [MULTIPLIER](exuarch:device/mac) takes two signed 16 bit numbers, multiplies them into a 32 bit product, and returns the product shifted right by 12, clamped to 16 bits. That makes it a multiplier for **4.12 fixed point** numbers: 4 bits before the binary point and 12 after, so 4096 is 1.0, 6144 is 1.5 and -8192 is -2.0, and the numbers run from -8 to just under 8. A product of two 4.12 numbers has 24 bits after the point, and the shift by 12 brings it back to 12. The Mandelbrot set never needs more than about 6.5, so 4.12 gives it as many bits after the point as 16 bits allow.

[MUL](exuarch:instruction/MUL) `Rd, Rs, Rt` is RISC-16's three register form: it loads Rs and Rt into the multiplier, multiplies, and puts the result in Rd, in 12 ticks with the fetch, as long as an `ADD`. Without it, RISC-16 has to multiply with a loop of shifts and adds, one round for each of the 16 bits, which takes many times as long.

## The Mandelbrot set

Every point c of the picture, with cx across and cy down, runs the same sum over and over: z = z² + c, starting from z = c. Written out for x and y, that is x' = x² - y² + cx and y' = 2xy + cy: three multiplications every step. Some points stay close to 0 for ever; they are the Mandelbrot set, and they are black. The rest sooner or later get further than 2 from 0, which is when x² + y² passes 4, and they are coloured by how soon that happened, from a palette of 32 colours: deep blue for the ones that leave at once, through white and orange, to dark brown, and round again.

Two details come from the fixed point. x² + y² can pass 32767 just before a point escapes, so the program compares it with 4 without a sign, `CMPI R7, 16385` and `BCC`. And the points are stepped through by adding: cx starts at the left edge and goes up by the step for every point, cy by the step for every row.

The program works out one row of points at a time into memory, then draws it. The screen's cursor moves right after every `PLOT` and wraps at the end of a row of pixels, so the picture fills the screen in order without a single `PX` or `PY`.

## Four pictures

The programs are the same but for their numbers. Three draw the whole set, from -2.5 to 1.25 across and -1.40625 to 1.40625 down; the fourth closes in on the tip at the top of the set:

| Program | Points | Each point | Steps | Ticks |
|---|---|---|---|---|
| [Mandelbrot, 80 x 60](<exuarch:program/Mandelbrot, 80 x 60>) | 4,800 | 8 x 8 pixels | 32 | 8,239,344 ticks |
| [Mandelbrot, 160 x 120](<exuarch:program/Mandelbrot, 160 x 120>) | 19,200 | 4 x 4 pixels | 32 | 25,605,802 ticks |
| [Mandelbrot, 640 x 480](<exuarch:program/Mandelbrot, 640 x 480>) | 307,200 | 1 pixel | 32 | 353,232,756 ticks |
| [Lightning at the top of the set](<exuarch:program/Lightning at the top of the set>) | 307,200 | 1 pixel | 128 | 583,757,144 ticks |

Run them with **Max** speed in [Run](exuarch:tab/Run); the clock panel shows how many ticks a second your browser reaches. A desktop browser runs about 2 million a second, and then the first picture takes about 4 seconds, the second about 13, the whole set at full size about 3 minutes and the close up about 5. On a slower computer or a phone it takes longer. The points inside the set are the slow ones: they always run every step.

## Closing in

[Lightning at the top of the set](<exuarch:program/Lightning at the top of the set>) shows the tip at the top of the set, around -0.1011 + 0.9563i, where the set branches like lightning into filaments full of tiny copies of itself. Its step is 1, which is 1/4096, the smallest difference 4.12 can represent, so the picture is 640/4096 = 0.15625 across: 24 times closer than the whole set at 640 x 480. Near the edge a point takes longer to escape or stay, so it runs up to 128 steps, and the palette going round every 32 steps keeps the detail coloured however long a point took.

That is as close as 16 bits get. A smaller step would land several points on the same 4.12 number, and the picture would turn into blocks. The deep zooms you may have seen use numbers of 64 bits and more.

## Things to try

1. Run the 80 x 60 picture with **Max** speed and watch it fill in a row of blocks at a time. Then step through one point with **Instruction** and follow x and y through the multiplier.
2. Close in on another place: change `left`, `top` and the step. Seahorse valley, between the big disc and the one to its left, is around -0.745 + 0.11i; at a step of 1 it is still mostly black, because its spirals are deeper than 4.12 can reach.
3. Give a picture more steps: change `MOVI R6, 32`. The edge gets finer, and the set's inside slower.
4. Draw a Julia set instead: keep c the same for every point, such as cx = -0.8 and cy = 0.156, and start z at the point.
5. Take MUL out and write the multiplication with `LSL`, `LSR` and `ADD` on [RISC-16](exuarch:package/RISC-16), then count the ticks.

## Read more

- [RISC-16](exuarch:package/RISC-16), the same machine without the multiplier.
- [GPU-16](exuarch:package/GPU-16), whose 3D pipeline has the same multiplier, set to 8.8, to turn its cube.
- [Registers and the ALU](exuarch:guide/registers-and-the-alu), and what an ALU can not do in one go.
- [ExµArch and real hardware](exuarch:guide/real-hardware): a multiplier that returns its product in a tick is one of the simplifications.
