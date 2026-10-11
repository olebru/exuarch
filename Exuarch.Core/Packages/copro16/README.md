# COPRO-16

A CPU with a **coprocessor**. The CPU starts slow work on a second piece of hardware and continues with its own program while it runs, the way the Amiga's blitter drew graphics or a floating point unit worked beside the main processor.

## The idea

Filling a rectangle takes one tick per pixel. If the CPU plotted every pixel itself it would do nothing else for thousands of ticks. Here it writes the job to [the blitter](exuarch:device/blit)'s registers in a handful of instructions, writes `GO`, and continues straight away with its own work. The blitter paints on a bus of its own, so their transfers never share a bus.

## The parts

- On the **main** bus: an accumulator CPU with [A](exuarch:device/a), [B](exuarch:device/b), [the ALU](exuarch:device/alu), [memory](exuarch:device/mem) and [the LCD](exuarch:device/lcd).
- [The blitter](exuarch:device/blit) sits between the buses. Its host side is on main, where the CPU writes its rectangle; its video side is on the **video** bus.
- [The screen](exuarch:device/fb) is on the video bus, and only the blitter drives it.

## How a job runs

The CPU sets up a job with one instruction per parameter, [BX](exuarch:instruction/BX), [BY](exuarch:instruction/BY), [BW](exuarch:instruction/BW), [BH](exuarch:instruction/BH) and [BC](exuarch:instruction/BC) (or the `BXI`-style versions with a number), and starts it with [GO](exuarch:instruction/GO).

From then on the blitter works by itself. Every tick it puts one value on the video bus and enables one of the screen's lines, just as microcode would: the column, then the row, then one pixel per tick along the row. The CPU can read whether it is still busy with [BST](exuarch:instruction/BST), and has to wait for it to be idle before writing the next job.

## Things to try

1. Run [Rectangles while the CPU writes](<exuarch:program/Rectangles while the CPU writes>) and step it in the [Run view](exuarch:tab/Run): while the blitter paints, the CPU prints the rectangle's name on the LCD. Watch the main and video buses carry values in the same ticks.
2. [Checkerboard](<exuarch:program/Checkerboard>) uses the overlap properly: the CPU works out the next square while the blitter fills the current one.
3. The CPU spends most of its time waiting on `BST`. The IRQ-16 package shows how an interrupt removes that waiting.

## Read more

- [Bus masters and coprocessors](exuarch:guide/bus-masters): how the blitter works on its own bus.
- [Bridges between buses](exuarch:guide/bridges): the other way to join two buses, with a register that sits on both.
- [Interrupts](exuarch:guide/interrupts): the next step: the blitter raising an interrupt when it is done.
