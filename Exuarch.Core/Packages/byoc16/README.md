# BYOC-16

The machine this project started with: a classic **accumulator** design with a single bus, the kind of computer you could build from a few dozen chips on a breadboard. It is the best place to start, because nothing about it is clever: every instruction is a short, plain sequence of data moves you can follow tick by tick.

## The idea

Almost everything goes through one register, **A**, the accumulator. Loads put a value in A, arithmetic combines A with **B** and puts the result back in A, and stores write A (or B) to memory. A small computer with one working register needs few control lines and little microcode, which is why so many early machines looked like this.

## The parts

Everything sits on one bus, `main[15:0]`:

- [PC](exuarch:device/pc), the program counter, points at the next cell of the program in [program memory](exuarch:device/mem).
- [The instruction register](exuarch:device/regi) is really the decoder's micro step counter: it counts one step per tick, and together with the flags it selects the row of the decoder ROM that runs next.
- [A](exuarch:device/rega) and [B](exuarch:device/regb) are the working registers. [The ALU](exuarch:device/alu) is wired to both and writes its flags to [the status register](exuarch:device/regsta).
- [The MMU](exuarch:device/mmu) holds banks of data memory. Bank 0 is the stack, and [SP](exuarch:device/regsp) points into it.
- The devices: [an LCD](exuarch:device/lcd) for text, [a 640 × 480 screen](exuarch:device/fb) and [a keypad](exuarch:device/keys).

## How an instruction runs

The [fetch routine](exuarch:instruction/FTC) runs first, always: it puts the PC on the bus as a memory address, and loads the cell there, the opcode, into the instruction register. The opcode *is* the ROM address where that instruction's steps begin, so the next tick runs its first step.

Take [LAI](exuarch:instruction/LAI), load A with a value: step past the opcode, address the cell after it, and load that cell into A. Its last step resets the instruction register to 0, which is where fetch starts again. [ADD](exuarch:instruction/ADD) is even shorter, a single step: the ALU drives A + B onto the bus and A takes it.

Conditional jumps such as [JEQ](exuarch:instruction/JEQ) show how the flags are used: the decoder has a different set of steps for Z=1 and Z=0, so the same opcode either loads the PC or skips the operand.

## Things to try

1. Load [Hello, world on the LCD](<exuarch:program/Hello, world on the LCD>) and step it with **Tick** in the [Run view](exuarch:tab/Run). Watch the control lines light up and the value travel along the bus.
2. Run [Fibonacci on the LCD](<exuarch:program/Fibonacci on the LCD>) at ⚡ Max. It prints numbers in decimal without a divide instruction, by subtracting each place value as often as it fits.
3. Open the [Decoder ROM](exuarch:tab/Run) panel under the schematic while stepping: each tick is one row of it.
4. Change something: give [INA](exuarch:instruction/INA) a sibling that adds 2, or make [PSA](exuarch:instruction/PSA) shorter, and see what breaks.

## Read more

- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register): how fetch and the instruction register step through an instruction.
- [Flags and conditions](exuarch:guide/flags-and-conditions): how JEQ and the other jumps select their steps.
- [Memory and banks](exuarch:guide/memory-and-banks): the MMU banks behind the stack.
- [Registers and the ALU](exuarch:guide/registers-and-the-alu): a tutorial that builds a machine like this one from scratch.
