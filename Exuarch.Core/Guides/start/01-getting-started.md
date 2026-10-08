# Getting started

ExµArch is a workbench for computers that don't exist yet. You wire up the hardware, write the microcode that makes it do things, write a program in the instruction set that microcode defines, and watch it run one clock tick at a time. Nothing is fixed: the number of buses, the registers, the instruction set, even whether there is an instruction set at all.

This handbook has three parts: tutorials that build a machine step by step, concept pages that explain one idea each, and a reference page for every kind of device. Search at the top of the contents covers all of them and the machine you have open. The small **?** buttons around the app open the page about whatever they sit next to.

## Build a machine, step by step

Each tutorial starts where the last one stopped, beginning from **New…** and the minimal CPU. Every step is checked by a test, so what you build will run. Prefer to watch? The same tutorials are a video series, [Build a computer in ExµArch](https://www.youtube.com/playlist?list=PLOZxNQ9ktMYM) on YouTube, one episode per tutorial.

1. [Ground zero](exuarch:guide/ground-zero): clear the microcode to nothing and watch the decoder's counter count on its own, then make a loop from one word.
2. [Fetch](exuarch:guide/fetch-routine): two steps that read the next opcode from memory.
3. [Opcodes are addresses](exuarch:guide/opcodes-are-addresses): `HLT`, and how the counter and the flags address the decoder ROM.
4. [Ending an instruction](exuarch:guide/ending-an-instruction): `NOP` and `JMP`, and what happens when the counter is not sent back to fetch.
5. [Your first machine](exuarch:guide/first-machine): add a display and an instruction that writes to it, then print "Hi".
6. [Registers and the ALU](exuarch:guide/registers-and-the-alu): two registers and an ALU, and a program that computes the characters it prints.
7. [Loops and flags](exuarch:guide/loops-and-flags): a conditional jump built from steps that only run for one value of a flag, and a countdown.
8. [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack): a stack pointer, `CALL` and `RET`.
9. [Reading the keypad](exuarch:guide/reading-the-keypad): input, and a program that echoes the keys you press.
10. [Taking an interrupt](exuarch:guide/taking-an-interrupt): an interrupt controller, a fetch routine that takes an interrupt, and a handler that prints the keys while the main loop never asks.
11. [A stack machine](exuarch:guide/stack-machine): a new machine whose values live on a data stack in memory, with PUSH, ADD and OUT that work on its top.

## The tabs

- [Hardware design](<exuarch:tab/Hardware design>) holds the devices and the buses between them. Drag a device from the palette onto the canvas and it lands on the nearest bus; select it to see its control lines, connections and parameters.
- [Microcode](exuarch:tab/Microcode) holds the instruction set: each instruction is a list of steps, and each step names the control lines that are on for one tick.
- [Program](exuarch:tab/Program) is assembly for the machine, using the mnemonics the microcode defines. **＋ New program** starts an empty one.
- The machine's **JSON**, the whole machine as one file kept in step with the editors, is under *This machine* in the getting started drawer.
- [Run](exuarch:tab/Run) runs it: **Tick** (→) runs one clock tick, **Instruction** (Shift+→) finishes the current instruction and fetches the next, **Space** runs and pauses, and **R** resets. Below the machine are the memory, the decoder ROM and a trace of the recent ticks. The slider sets the clock speed. **⚡ Max** (M) ignores it and runs as fast as the browser allows, and to get there it shows less: the view is redrawn only every so often, and the trace keeps one tick per frame instead of every tick (as it also does above 200 Hz). A double buffered screen still shows every frame it finishes. From a pause, **Tick** and **Instruction** record every tick again.

## How the pieces fit

A machine is devices on [buses](exuarch:guide/buses-and-ticks). Every tick has two halves: devices whose output line is on drive their bus, then devices whose load line is on take the value. What a device can do is its set of [control lines](exuarch:guide/devices-and-control-lines).

The [microcode](exuarch:guide/microcode) decides which lines are on in each tick. A [fetch routine](exuarch:guide/fetch-and-the-instruction-register) loads the next opcode, and the instruction register steps through that instruction's microcode. [Flags and conditions](exuarch:guide/flags-and-conditions) let a step run only when a flag has a certain value, which is how a machine makes decisions. An instruction's [operands](exuarch:guide/operands) are the cells that follow its opcode.

Programs are written in [assembly](exuarch:guide/assembly): mnemonics, labels, `.DATA` and `.STRING`. They are loaded into [memory](exuarch:guide/memory-and-banks), which can also be split into banks.

Beyond one bus and one CPU: [bridges](exuarch:guide/bridges) move values between buses, [bus masters](exuarch:guide/bus-masters) such as the blitter work on a bus of their own, and [interrupts](exuarch:guide/interrupts) let devices stop the program to be served. A whole machine, with its note and programs, is saved as a [package](exuarch:guide/packages). Packages are how machines are shared, and one from someone else is safe to open: [running other people's machines](exuarch:guide/safety) says why. And [the graphics pipeline](exuarch:guide/graphics-pipeline) puts several of these together into a small 3D GPU.

Everything you build here works the way real hardware does at the level of registers and control lines, with a few simplifications that keep the focus on the architecture. [ExµArch and real hardware](exuarch:guide/real-hardware) lists them, and what a real build would add.

## The examples

The built in machines each take a different direction. Load one from **Machines** in this drawer, take it apart, and start your own from a copy with **New…**. Everything you change is kept in the browser; **Export** saves a machine as a file. They come in three levels, and the drawer folds away the two harder ones until you open them.

**Simple**: one bus and a few registers.

- [TINY-16](exuarch:package/TINY-16): the one to start with, one register and seven instructions.
- [BYOC-16](exuarch:package/BYOC-16): a classic accumulator machine on one bus.
- [STACK-16](exuarch:package/STACK-16): a stack machine, where every instruction works on the top of a data stack, and calls have a return stack of their own.

**Advanced**: more buses, other ways to build a CPU, and devices that work on their own.

- [RISC-16](exuarch:package/RISC-16): a load/store machine with a register file and three register instructions: `ADD R0, R1, R2`.
- [CISC-16](exuarch:package/CISC-16): RISC-16's opposite, six addressing modes on every instruction, memory to memory moves, and stack frames for recursion.
- [HARVARD-16](exuarch:package/HARVARD-16): three buses, with separate program and data memory.
- [MOVE-16](exuarch:package/MOVE-16): no instruction set, only moves between ports.
- [COPRO-16](exuarch:package/COPRO-16): a CPU that hands work to a blitter on its own bus.
- [IRQ-16](exuarch:package/IRQ-16): interrupts from a timer, the keypad and the blitter, and a falling blocks game built on them.

**Ludicrous**: a 3D graphics pipeline and how far it can be pushed, the Mandelbrot set in colour, and a machine that has never existed before.

- [GPU-16](exuarch:package/GPU-16): a small 3D pipeline, with shaded triangles, a depth buffer and a spinning cube.
- [FLIP-16](exuarch:package/FLIP-16): GPU-16 with a double buffered screen, so each frame appears whole.
- [TURBO-16](exuarch:package/TURBO-16): FLIP-16 rebuilt for speed, the same cube in under a fifth of the ticks.
- [DSP-16](exuarch:package/DSP-16): RISC-16 with a hardware multiplier, drawing the Mandelbrot set in colour.
- [WORM-16](exuarch:package/WORM-16): a new kind of machine with no jumps, whose program crawls round a ring memory and decides, instruction by instruction, what lives on.
