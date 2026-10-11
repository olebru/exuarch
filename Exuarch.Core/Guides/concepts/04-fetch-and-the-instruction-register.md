# Fetch and the instruction register

A machine runs a program by repeating the same loop: fetch the next opcode from memory, run that instruction's steps, go back and fetch again. In ExµArch that loop is not built in. It is made from one device, the instruction register, and one routine you write, fetch. Once you see how they work together you can bend the loop, for example to fetch the next instruction while the current one finishes.

## The decoder

In the [hardware design](<exuarch:tab/Hardware design>) the decoder is a card of its own, next to the devices. It sits on no bus: its control lines reach every device directly. Its three sockets name the devices it works with: **status**, the status register whose flags select which steps run; **steps**, the instruction register; and **interrupts**, an optional interrupt controller. Drag a socket onto a device to wire it, or select the card and choose them in the inspector.

## The instruction register

The `instructionRegister` is the decoder's micro step counter. Its value is the micro step address the decoder looks up for the next tick, and it changes at the end of every tick in one of three ways:

- `reset`: it becomes 0, the first step of the fetch routine.
- `load`: it takes the value on its bus, which is an opcode, the address of an instruction's first step.
- neither: it counts up by one, to the next step.

If both lines are on, `reset` takes priority. The counting needs no signal, which is why an instruction's steps simply run one after the other. The machine's decoder names the one device it uses as its instruction register (`decoder.instructionRegister`).

Despite its name it does not hold the instruction the way a register in a textbook CPU does. It holds where the decoder is inside the microcode, and loading an opcode into it is a jump to that instruction's steps.

## The fetch routine

Fetch is the routine at micro step address 0. In the minimal CPU that **New…** creates it is two steps:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, ir.load, pc.inc
```

Step 1 puts the program counter on the bus as the memory address. Step 2 puts the cell at that address, the opcode, on the bus and loads it into the instruction register, and moves the program counter past it. In the next tick the decoder is at the opcode, so the instruction's first step runs.

The program counter is an ordinary `register`. Nothing makes it special except that fetch uses it that way; the assembled program is loaded into the program memory from address 0, and every register starts at 0, so fetch begins with the first cell.

## Ending an instruction

An instruction's last step sends the decoder back to fetch with `ir.reset`, as in the minimal CPU's `JMP`:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, pc.load, ir.reset
```

If an instruction does not reset or load it, the counter keeps counting into the next instruction's steps. The microcode editor warns about any instruction, or flag variant of one, that never resets or loads the instruction register and never disables the clock.

An instruction may also end with `ir.load` instead: if its last step can put the next opcode on the bus, it fetches its successor itself and the fetch routine is skipped. [HARVARD-16](exuarch:package/HARVARD-16) does this, because its program memory has a bus of its own. The last step of its `POP` reads the stack on one bus while it loads the next opcode on another:

```
dmem.output, a.load, sp.inc, pmem.output, ir.load, pc.inc
```

## Reading operands

Operands are the cells after an opcode, and an instruction reads them the same way fetch reads the opcode: put the program counter on the bus as an address, read the cell, move the program counter on. Where the program counter points when the instruction starts depends on your fetch routine, and the built in machines do it both ways:

- The minimal CPU increments the program counter in fetch, so an instruction starts with it on the first operand.
- [BYOC-16](exuarch:package/BYOC-16)'s fetch does not, so its instructions start with `pc.inc`. Its `LAI` is `pc.inc`; `pc.output, mem.loadmar`; `mem.output, rega.load, regi.reset, pc.inc`.

Either works, as long as every instruction leaves the program counter on the next opcode.

## Opcodes are addresses

An opcode is the micro step address where the instruction's steps begin. Fetch comes first at 0 and each instruction takes as many addresses as its longest flag variant has steps, so opcodes follow from the order and length of the instructions. The assembler looks them up from the microcode every time, so you never write them yourself.

## Watching it run

In the [Run](exuarch:tab/Run) tab, **Tick** runs one tick and **Instruction** runs until the next opcode has been loaded into the instruction register. The memory view marks the cell the current instruction was fetched from, and the Decoder ROM tab shows the micro step register's 16 bits next to the flags.

The instruction register's value also determines when interrupts are checked: a request is sampled when it is 0, at the start of fetch. See [Interrupts](exuarch:guide/interrupts).

## See also

- [Microcode](exuarch:guide/microcode)
- [Flags and conditions](exuarch:guide/flags-and-conditions)
- [Operands](exuarch:guide/operands)
- [The instruction register](exuarch:reference/instructionRegister)
- [Fetch](exuarch:guide/fetch-routine), a tutorial that builds fetch tick by tick
- [Your first machine](exuarch:guide/first-machine), a tutorial
