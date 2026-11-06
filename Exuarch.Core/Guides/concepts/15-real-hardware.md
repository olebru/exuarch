# ExµArch and real hardware

ExµArch works at the level of registers, buses and control lines: what a computer architect draws, one level above the gates. At that level nothing is hidden. Every value is on a bus or in a register you can see, and every step is a set of control lines you wrote. But a simulator has to define things that real electronics leave to physics, such as what a wire carries when nothing drives it. This page says where ExµArch behaves like real hardware, where it makes things simpler, and what you would have to add to build your design in wires.

## What works the way real hardware does

- **Fixed widths wrap.** Every bus, register and memory cell is 16 bits, and counting past the end wraps: `FFFF` plus one is `0000`, and `0000` minus one is `FFFF`. A real counter has no 17th bit to carry into either.
- **One device drives a bus.** Two outputs on the same wires is a short circuit in real hardware too. See [buses and ticks](exuarch:guide/buses-and-ticks).
- **Drive, then latch.** In the first half of a tick devices put values on the buses; in the second, devices take them. That is how a synchronous circuit with edge triggered registers works: outputs settle on the wires during the clock cycle, and every register captures the value on its input at the same clock edge. It is also why a value can not pass through two registers in one tick, here or in silicon.
- **Microcode in a ROM.** The decoder looks up each step's control lines in a ROM addressed by the flags and the micro step counter, as many real processors have done, from minicomputers to the classic microcoded CPUs. See [microcode](exuarch:guide/microcode).
- **Address lines that are not connected.** A memory whose size is a power of two only decodes as many address bits as it has pins for, so it repeats across the 16 bit address space, just as a chip with fewer address pins than the bus has lines. See [memory and banks](exuarch:guide/memory-and-banks).
- **Interrupts between instructions.** The machine checks for an interrupt request when an instruction ends and fetch begins, as real processors do, so an instruction is never cut in half. See [interrupts](exuarch:guide/interrupts).

## Where ExµArch makes things simpler

Each of these is a choice built into the simulator. None of them hides what your machine does, but a real build would have to deal with them.

### Everything starts at 0

Every register, and every memory cell, holds 0 when the machine is built and every time you press **Reset** in Run. Real flip-flops and memory come up holding whatever the power-on surge left in them. A real machine pulls the clear pin of the registers that matter from a reset circuit, which is what a register's `reset` line stands for, and its program sets up anything else before using it. A stack pointer that is never set works here, and [memory and banks](exuarch:guide/memory-and-banks) explains why, and what a real machine does instead.

### Nothing takes time

The ALU's result is on the bus in the same half tick its operands are read, and a memory puts out the cell at its address at once. Real logic has propagation delays, memories have access times, and registers need their input steady for a moment before and after the clock edge. Those delays set the fastest clock a real circuit can run at, and slow memories need extra wait states. ExµArch has no fastest clock: the speed you set and see in Run is how fast the simulator steps, not a property of your circuit. What carries over is the number of ticks, which is the true measure of a design here. The one device that reads real time is the [`rtc`](exuarch:reference/rtc), a real time clock: a program that waits for it takes as long in seconds whatever the speed, and as many more ticks as the simulator runs faster.

### A bus nobody drives reads 0

Each bus is cleared at the start of every tick, so reading one that nothing drives gives 0, and the microcode editor warns you. Real wires that nothing drives float: they pick up noise, and old TTL inputs tend to read a floating line as 1. Real buses add pull up or pull down resistors so that they read a known value, and ExµArch behaves like a bus with a pull down on every line.

### A short circuit stops the machine

When two devices drive the same bus, ExµArch stops with a "puff of blue smoke" error that names both. In real hardware the two outputs contend: the bus carries a level that is neither 0 nor 1, the chips heat up, and they may be damaged. The microcode editor reports the conflict before you run, which no real circuit does for you.

### A register applies every line that is on in one tick

If one step turns on more than one of a register's `load`, `reset`, `inc` and `dec`, all of them happen, in that order, at the end of the tick. So `x.load` and `x.inc` together store the bus value plus one, and `inc` and `dec` together leave the register as it was. A real counter chip applies only one. The 74LS163, for example, gives clear priority over load, and load priority over counting. If your design relies on two of them together, a real build needs logic that does the same.

### Addresses wrap at any size

A ram can have any size from 1 to 65536 cells, and its address always wraps at that size, so no address is ever out of range. For a power of two that is what the wires do. For another size, say 3000, it would take a divider in the address path, which no real memory has: real hardware would leave the addresses past the end unconnected, or put other devices there.

### Every bus is 16 bits

Real machines often mix widths, for example 8 bit data with 16 bit addresses, and need extra steps or registers to move a wide value over a narrow bus. Here every bus, register and memory cell is one 16 bit word, and addresses count words, not bytes.

### Big devices are described by what they do

A register is close to a row of flip-flops, but the ALU, the blitter, the rasterizer and the other large devices are modelled by what they do in each tick, not by their gates. Each one's control lines and behaviour are on its page in the device reference, such as the [alu](exuarch:reference/alu), so you can always see exactly what it will do, but not how a chip would do it. That is the floor ExµArch stops at: below it is digital electronics, which is a subject of its own.

## Why it is this way

ExµArch is first a playground for trying out new ideas in computer architecture, and second a way to learn how computers work without black boxes. The simplifications above serve the first goal: they let a new design run as soon as its microcode is right, without timing, power-on and electrical problems getting in the way. This page serves the second: nothing is simplified without saying so. If your idea works here, you know it works at the level of registers and control lines, and this list is what stands between it and a circuit board.

## See also

- [Buses and the two-phase tick](exuarch:guide/buses-and-ticks)
- [Devices and control lines](exuarch:guide/devices-and-control-lines)
- [Memory and banks](exuarch:guide/memory-and-banks), and why a stack needs no setup
- [Interrupts](exuarch:guide/interrupts)
- [The register reference](exuarch:reference/register) and [the ram reference](exuarch:reference/ram)
