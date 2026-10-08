# Interrupts

Without interrupts, a program finds out about the world by asking. It reads the keypad in a loop, or asks the blitter whether it is done. An interrupt turns that around: a device raises a request, and the CPU drops what it is doing at the next instruction boundary to handle it. In ExµArch there is no built in interrupt mechanism. You get an interrupt controller that collects requests and a flag condition the microcode can test. What an interrupt actually does is up to your fetch routine.

## Where requests come from

Five device types can raise an interrupt request:

- a [`timer`](exuarch:reference/timer), every period ticks while it runs;
- an [`rtc`](exuarch:reference/rtc), a real time clock, every interval milliseconds of real time while it runs;
- a [`keypad`](exuarch:reference/keypad), when a key goes down (holding it, or key repeat, does not ask again);
- a [`blitter`](exuarch:reference/blitter), when it finishes a job;
- a [`rasterizer`](exuarch:reference/rasterizer), when it finishes its list of triangles.

A request is an event, not a level. Each one is picked up once by the interrupt controller it is connected to.

## The interrupt controller

An [`interruptController`](exuarch:reference/interruptController) has four sources, connected as `irq0` to `irq3`. Each must name a timer, real time clock, keypad, blitter or rasterizer. Each source has a pending bit, bit 0 for `irq0` and so on. When a source raises a request, its bit is set and stays set until the program clears it.

Its control lines are:

- `enable` and `disable` switch interrupts on and off. They start **off**.
- `loadmask` takes a mask from the low four bits of the bus: bit n set lets `irq`n interrupt. It starts as all four.
- `output` puts the pending bits that are not masked off on the bus, so a handler can see who asked.
- `ack` clears the pending bits that are set in the bus value.

The controller is **requesting** when interrupts are enabled and at least one pending bit is not masked off.

## The I condition

To let the microcode see the controller, name it in the decoder as `interrupts`:

```json
"decoder": { "status": "flags", "instructionRegister": "ir", "interrupts": "pic" }
```

The decoder then has a fifth condition next to N, V, C and Z: **I**, which is 1 while the controller is requesting. It is the top bit of the decoder's status, shown as `INVCZ` in the Run view's decoder ROM panel. A step can require it like any flag, with `"when": { "I": true }` or `"when": { "I": false }`. See [flags and conditions](exuarch:guide/flags-and-conditions).

I is sampled when the instruction register is 0, at the start of the fetch routine, and it is held for the whole instruction that follows. A request that arrives halfway through an instruction does not change which steps it runs; it waits for the next fetch. Without `decoder.interrupts`, I is always 0.

## Taking the interrupt in fetch

Because I is fixed from the start of fetch, the fetch routine is the natural place to branch on it. [IRQ-16](exuarch:package/IRQ-16) gives its fetch two sets of steps:

- **I=0**: the usual two steps. Put the PC on the bus as an address, then load the opcode into the instruction register and step the PC past it.
- **I=1**: push the PC and the flags on the stack, then load the PC from the `vec` register, which holds the handler's address. Switch interrupts off with `pic.disable`, and `ir.reset`.

```json
{ "when": { "I": true }, "signals": ["flags.output", "mem.load"] },
{ "when": { "I": true }, "signals": ["vec.output", "pc.load", "pic.disable", "ir.reset"] }
```

After `ir.reset` the instruction register is 0 again, so I is sampled again. Interrupts are now off, so I reads 0, and the ordinary fetch loads the handler's first instruction. Switching interrupts off before sampling again is what stops the handler from being interrupted by the same request.

## Handling and returning

A handler typically:

1. reads the pending bits with the controller's `output` line to see which source asked;
2. does its work;
3. clears the bits it handled with `ack`;
4. returns.

IRQ-16's instruction set has one instruction for each of these jobs:

- `SETV` loads the handler's address into `vec`.
- `EI` and `DI` switch interrupts on and off with `pic.enable` and `pic.disable`.
- `CAUSE` loads the pending bits into A with `pic.output`.
- `ACK` clears the bits that are set in A, using `a.output` with `pic.ack`.
- `RTI` returns from the handler.

`RTI` pops the flags and the PC, and switches interrupts back on in its last step. That step also does `ir.reset`, so the next fetch samples I straight away and a request that came in meanwhile is taken at once.

If you forget `ack`, the bit stays pending. As soon as interrupts are enabled again, the CPU is interrupted again.

## The timer

A timer's **period** parameter is the number of ticks between requests, from 0 to 65535 (1000 if you leave it out). Its lines:

- `loadperiod` takes a new period from the bus.
- `start` starts counting from 0.
- `stop` stops counting.
- `output` puts the current count on the bus.

A timer starts stopped. While it runs it counts every tick, and when the count reaches the period it raises a request and starts again from 0. A period of 0 never fires. IRQ-16 connects a timer with a period of 20000 to `irq0`, the keypad to `irq1` and the blitter to `irq2`, so its main program never has to poll any of them.

## The real time clock

A timer counts ticks, so how often it fires in seconds depends on how fast the machine runs: 1000 ticks is a minute at 16 Hz and half a millisecond at full speed. An [`rtc`](exuarch:reference/rtc) keeps to the wall clock instead. Its **interval** parameter is the number of milliseconds between requests, from 0 to 65535 (1000, a second, if you leave it out). Its lines:

- `loadinterval` takes a new interval, in milliseconds, from the bus.
- `start` starts timing an interval from now.
- `stop` stops timing.
- `output` puts the interval on the bus.

A real time clock starts stopped. While it runs, it looks at the time in every tick, and once the interval has passed it raises a request and starts the next interval where the last one ended, so its requests keep in step with the clock however fast the machine ticks. An interval of 0 never fires.

It can only ask in a tick. While the machine is paused, or between ticks at a low speed, time goes on without it: the first tick after an interval has passed raises one request, however many intervals went by, and the next interval is counted from that tick. A program that counts the requests therefore counts the intervals it was running for, not the time that went past while it was paused.

Because it keeps to real time, a program that waits for a real time clock takes as long in seconds at any speed, and as many more ticks as the machine runs faster: the one thing in ExµArch that ties the simulation to the time outside it.

## See also

- [Taking an interrupt](exuarch:guide/taking-an-interrupt): the tutorial that builds a fetch routine that takes an interrupt, and a handler
- [Flags and conditions](exuarch:guide/flags-and-conditions)
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack)
- [interruptController reference](exuarch:reference/interruptController), [timer reference](exuarch:reference/timer) and [rtc reference](exuarch:reference/rtc)
- [IRQ-16](exuarch:package/IRQ-16)
