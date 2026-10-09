# Memory and banks

Registers hold a handful of values; memory holds the program, its data and the stack. In ExµArch a memory is a device like any other: it sits on a bus, has its own address register, and does nothing until a control line in a microcode step tells it to. Knowing how that address register behaves from tick to tick is most of what it takes to write load and store instructions.

## Cells and addresses

Every memory cell holds one 16 bit word, like every bus and register in the machine. Addresses count cells, not bytes: address 5 is the sixth word. A cell that has never been written reads as 0.

## The ram device

A [`ram`](exuarch:reference/ram) device has a **size** parameter, the number of cells, from 1 to 65536 (4096 if you leave it out), and six control lines:

- `loadmar` takes an address from the bus into the memory address register, the MAR. The address wraps at the size, so in a 4096 cell memory, 4100 addresses cell 4, and 65535 addresses cell 4095. That is why a stack pointer can start at 0 like every register: its first decrement wraps to 65535, the top of memory. *Why a stack needs no setup*, below, explains how much of that is real hardware.
- `output` puts the cell at the MAR on the bus.
- `load` stores the bus value in the cell at the MAR.
- `outputmar` puts the MAR itself on the bus.
- `incmar` and `decmar` step the MAR one cell forward or back at the end of the tick, wrapping at the ends of memory. *Walking through memory*, below, shows what they are for.

The MAR belongs to the memory. Each memory keeps its own address until the next `loadmar`, so two memories on the same bus never get in each other's way.

## Timing: address first, then data

A tick has two halves: devices drive the buses, then devices latch (see [buses and ticks](exuarch:guide/buses-and-ticks)). `output` drives the cell at the address the MAR held when the tick began. `load` stores into the address the MAR held when the tick began. The new address from `loadmar` only arrives at the end of the tick.

So a memory access takes two steps: set the address, then read or write. The fetch routine of the minimal CPU does exactly that:

```json
{ "signals": ["pc.output", "mem.loadmar"] },
{ "signals": ["mem.output", "ir.load", "pc.inc"] }
```

If you put `loadmar` and `load` in the same step, the value goes to the old address.

## Walking through memory

Reading a string or a table means reading cell after cell. Without help, every cell costs a step to put the next address in the MAR, and a register to remember where you are. `incmar` and `decmar` step the MAR itself, after the read or write in the same tick:

```json
{ "signals": ["rom.output", "a.load", "rom.incmar"] }
```

That one step reads the cell at the MAR into A and leaves the MAR on the next cell, ready for the next read. Hardware calls this post-increment addressing. A write works the same way: `load` stores at the old address, then the MAR moves. With `decmar` before a read instead of after it, in a step of its own, the MAR walks backwards, the way a stack pops. When `loadmar` and `incmar` are in the same step, the MAR ends one past the address loaded.

The MAR is the memory's, not the program's. The program memory's MAR moves at every fetch, so walking only lasts within an instruction there. A second memory, such as a ROM or a data buffer, keeps its MAR between instructions, and a program can walk through it one instruction at a time. [ROM-16](exuarch:package/ROM-16) does both: NEXT reads its ROM forwards, PUT fills a buffer forwards, and BACK reads that buffer backwards.

## The rom device

A [`rom`](exuarch:reference/rom) is memory that nothing can write. It has the ram's lines without `load`, and its cells are filled before the machine starts, from the device's **contents** in the machine definition. Contents are written like data in a program, in lines of `.DATA` and `.STRING` with labels and comments:

```json
"contents": [
  "hello:  .STRING \"Hi\"",
  "table:  .DATA 0, 50, 100"
]
```

Labels in the contents name ROM addresses, so the cells list in the editor shows where each one lands. They are not visible to the program, which lives in another memory. Select a rom in **Hardware design** and press **Edit contents…** to edit them with the cells shown as they fill. **Insert a table** writes sine, cosine, ramp and squares tables. A problem in the contents, such as an instruction or more cells than the rom holds, is a problem with the machine, and it will not run until it is fixed.

## Why a stack needs no setup

STACK-16 and the machine in [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack) never set their stack pointer, and still their first push lands on the last cell of memory. Nothing is hidden here; three plain behaviours add up to it:

1. Every register starts at 0, when the machine is built and every time you press Reset in Run.
2. A register is 16 bits wide, so `dec` from 0 wraps round to 65535 (`FFFF`).
3. `loadmar` wraps the address at the memory's size, so 65535 in a 4096 cell memory is cell 4095.

A push uses all three by moving the stack pointer down *before* it writes: `sp.dec`, then `sp.output` `mem.loadmar`, then the value with `mem.load`. Each later push goes one cell lower, so the stack grows down from the top while the program sits at the bottom. A pop reads and then moves it up again with `sp.inc`.

### How much of this is real hardware

Most of it is how real electronics behave:

- **The wrap of the register.** A 16 bit counter, say four 4 bit up/down counter chips chained together, has no 17th bit to borrow from. Counting down from `0000` gives `FFFF`, exactly as here.
- **The wrap of the address.** 4096 is 2¹², so a 4096 word memory chip has 12 address pins. On a 16 bit address bus the top 4 lines are simply not connected, and the chip never sees them: `FFFF` and `0FFF` are the same cell to it. The memory shows up again and again across the address space, which is called mirroring. For a size that is a power of two, taking the address modulo the size is the same as dropping the high bits, so ExµArch does what the wires would do.
- **Moving down before writing, from 0.** Real processors such as the Z80, the 8080 and x86 decrement their stack pointer before a push, and setting it to 0 is a known way to put the stack at the very top of memory without working out where that is.

### Where ExµArch is kinder than hardware

Two things are simplified, and it helps to know which:

- **Registers starting at 0.** Real flip-flops come up in an unpredictable state when the power comes on. A real machine either has a reset circuit that pulls the clear pin of its registers, which is what the `reset` control line stands for here, or its program loads the stack pointer before the first push. ExµArch builds every machine as if its reset button had just been pressed.
- **Sizes that are not a power of two.** A ram can have any size from 1 to 65536, and its address always wraps at that size, so no address is ever out of range. Wrapping at, say, 3000 would take a divider in the address path, which no real memory has; real hardware would leave the addresses past the end unconnected or put other devices there. It also stops the stack from starting at the top: in a 3000 cell memory, 65535 is cell 2535, and the 464 cells above it are only reached once the stack has wrapped down through cell 0. Use a power of two for a stack that starts at the top. Every built in machine does.

### What nothing guards against

Nothing stops the stack from running into the program. Push often enough and it writes over the code at the bottom of memory. Pop more than was pushed and the stack pointer climbs past 0, so the pops read the program's first cells as if they were data. A real processor without memory protection behaves the same way, crash included.

### Try it

- Give a ram 3000 cells and watch where the first push goes in the **Memory** panel.
- Add an instruction that loads the stack pointer from its operand, and put the stack wherever you like.
- Make the stack grow upwards: write first, then `sp.inc`, and pop with `sp.dec` before reading.
- Guard the stack: compare the stack pointer with a limit in the ALU and branch on the flag, see [flags and conditions](exuarch:guide/flags-and-conditions).

## Program memory

The machine's `programMemory` names the memory the assembled program is loaded into, starting at address 0. It is usually a `ram` device, and the program must fit in it. It can be a `rom`: the program then overwrites the start of its contents, and nothing can change it while it runs. The same memory can hold data too: a `.DATA` line in the program is just more cells in it. See [assembly](exuarch:guide/assembly).

A machine can have as many memories as you like. HARVARD-16 keeps its program in one memory on the instruction bus and its data in another on the data bus, so a fetch and a data access can happen in the same tick.

## Banks: the mmu device

An [`mmu`](exuarch:reference/mmu) is several ram banks behind one chip select register. Its parameters are **banks**, from 1 to 256 (16 if you leave it out), and **bankSize**, the cells per bank (4096 by default).

The memory lines, `loadmar`, `output`, `load`, `outputmar`, `incmar` and `decmar`, work on the bank the chip select register points at. Three more lines manage that register:

- `loadcs` takes a bank number from the bus. Numbers wrap at the number of banks.
- `outputcs` puts the selected bank number on the bus.
- `select0stack` selects bank 0.

Each bank has its own MAR. Switching banks does not move an address from one bank to the other.

Within a tick, `select0stack` applies first. `output` and `outputmar` use the bank that was selected at the start of the tick, and `loadmar` and `load` use the bank selected after `loadcs` has latched. So `loadcs` and `loadmar` in the same step address the new bank.

BYOC-16 uses an mmu for data and its stack. Its push instruction, `PSA`, saves the selected bank, switches to bank 0 with `select0stack`, stores A at the stack pointer, and then selects the saved bank again. That way a program can work in any bank and still share one stack in bank 0.

An mmu can not be the program memory, which has to be a plain `ram`.

## Watching memory in Run

The [Run](exuarch:tab/Run) tab's **Memory** panel shows every ram, rom and mmu in the machine, with a button for each. Rows show 16 cells in hexadecimal and an ASCII column with the low 8 bits of each cell.

Cells are marked for the program counter, the MAR, the instruction that is running and the cells just written. For an mmu you choose the bank, and **selected** jumps to the one the chip select register points at. Large memories are split into pages of 256 cells, and **go to MAR** turns to the page the address register is in. Each tick's memory writes also appear in the **Last tick** panel, with the bank for an mmu.

## See also

- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks)
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack)
- [ExµArch and real hardware](exuarch:guide/real-hardware), every place the simulator simplifies
- [ram reference](exuarch:reference/ram), [rom reference](exuarch:reference/rom) and [mmu reference](exuarch:reference/mmu)
- [BYOC-16](exuarch:package/BYOC-16), [HARVARD-16](exuarch:package/HARVARD-16) and [ROM-16](exuarch:package/ROM-16)
