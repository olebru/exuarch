# Microcode

Microcode is where a machine's hardware turns into an instruction set. Each instruction is a short list of steps, and each step lists which control lines are on for one tick. The assembler, the decoder and the Run view all work from this one description, so changing the microcode changes what every program means.

## Instructions and steps

An instruction has a **mnemonic**, a single word such as `LAI` or `MOV_R1_R0`, an optional **description**, and a list of **steps**. A step is a list of signals, each written `device.line`, and an optional comment. One step runs per tick, and every signal in it is on for that tick only.

`LAI` from [BYOC-16](exuarch:package/BYOC-16) loads register A with the value that follows the opcode:

```
step 1: pc.inc
step 2: pc.output, mem.loadmar
step 3: mem.output, rega.load, regi.reset, pc.inc
```

Step 1 moves the program counter onto the operand, step 2 addresses it, step 3 reads it into A, moves the program counter past it and resets the instruction register so that the fetch routine runs next. See [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register) for why that last signal matters.

An instruction also specifies how many **operands** follow its opcode in memory, and optionally whether each one is a value or an address; see [Operands](exuarch:guide/operands).

Steps can have a flag condition, such as Z=1, so that they only run when the flags match. That is how conditional jumps work; see [Flags and conditions](exuarch:guide/flags-and-conditions).

## The fetch routine

One routine is special: **fetch**. It is not an instruction you write in a program. It comes first, at opcode 0, and it loads the next opcode into the instruction register. Every instruction ends by returning to it. In the [Microcode](exuarch:tab/Microcode) tab it is at the top of the list, marked *Fetch*.

## Opcodes

You never choose opcodes. Each instruction is assigned a block of consecutive micro step addresses, as many as its longest flag variant has steps, and its opcode is where its block starts. Fetch is at 0; if fetch has two steps, the first instruction's opcode is 2, and so on down the list. The opcode is shown next to each instruction.

So opcodes change when you add a step to an instruction or reorder the list by dragging. That is harmless: programs are assembled from mnemonics, and the assembler reads the current opcodes from the microcode every time.

The toolbar meter counts the micro step addresses used, out of the 65536 a 16 bit instruction register can select.

## Working in the editor

- **＋ Instruction** adds an instruction; **Duplicate** and **Delete** act on the selected one. Drag an instruction in the list to move it.
- **＋ Add step** adds a step. Steps can be dragged, moved with ↑ and ↓, duplicated and deleted.
- Add a signal to a step by dragging it from the **Control lines** palette on the right, by clicking it there, or by typing `device.line` in the step. ▲ marks a line that drives a bus, ▼ one that reads a bus. Double-click a signal to show its device in the hardware design.
- Under each step the editor shows what moves on each bus: which device drives it and which devices read it.
- **Preview flags** shows which steps run for a given set of flags, and numbers them by tick.
- Undo and redo are shared with the hardware design. To save or share microcode, export the package from the package menu next to the package name.

## Checks

The editor checks the microcode against the machine as you type. The main errors, which stop the machine from being built, are:

- a signal that is not written `device.line`, names an unknown device, or names a line the device does not have;
- two signals in one step that drive the same bus;
- an instruction with no steps, a mnemonic that is not a single word or is used twice, and a missing fetch routine;
- an operand count that differs from the number of operand types.

The main warnings, which let the machine run, are:

- a step that reads a bus nothing drives in that step (the read returns 0);
- an instruction, or one of its flag variants, that never resets or loads the instruction register or disables the clock, so it runs on into the next instruction's steps;
- steps after the one that returns to fetch, which never run;
- a signal listed twice in one step.

Click a problem in the list to jump to the instruction and step.

## See also

- [Devices and control lines](exuarch:guide/devices-and-control-lines)
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [Flags and conditions](exuarch:guide/flags-and-conditions)
- [Registers and the ALU](exuarch:guide/registers-and-the-alu), a tutorial
- [MOVE-16](exuarch:package/MOVE-16), where every instruction is a single move between two ports
