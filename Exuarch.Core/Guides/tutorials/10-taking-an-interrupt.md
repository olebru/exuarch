# Taking an interrupt

Watch this tutorial as a video: [episode 10 of Build a computer in ExµArch](https://www.youtube.com/watch?v=w0668GwwJgg) on YouTube.

The program from [Reading the keypad](exuarch:guide/reading-the-keypad) spends its whole life asking the keypad whether a key is down. An interrupt turns that round: the keypad asks for attention when a key goes down, the CPU finishes the instruction it is on, saves where it was, runs a handler, and comes back. In this tutorial the machine gets an interrupt controller, a fetch routine that takes an interrupt, and a program whose main loop never looks at the keypad, yet prints every key you press.

Skipped a tutorial, or lost your machine? Load [Tutorial 10 · Taking an interrupt](<exuarch:package/Tutorial 10 · Taking an interrupt>), under **Tutorial** in **Machines**: it is the machine this tutorial starts from.

## Add an interrupt controller and a vector register

1. Open [Hardware design](<exuarch:tab/Hardware design>) and drag an **interruptController** onto the bus. Set its ID to `pic` and connect `irq0` to `keypad`.
2. Drag a **register** onto the bus and set its ID to `vec`. It will hold the handler's address.
3. Select the decoder and set its **Interrupts** to `pic`.

The [interrupt controller](exuarch:reference/interruptController) collects requests from up to four devices into pending bits: the keypad asks when a key goes down, and bit 0 stays set until the program clears it. Interrupts start switched off. Naming the controller in the decoder gives the microcode a fifth condition next to N, V, C and Z: **I**, which is 1 while interrupts are on and a request is pending. It is sampled when a fetch starts, so a request that arrives halfway through an instruction waits for the next one. [Interrupts](exuarch:guide/interrupts) has the details.

## Teach fetch to take an interrupt

Open [Microcode](exuarch:tab/Microcode) and select `FETCH`. Its two steps read the next opcode; from now on they should only do so when nothing is asking. Click the **I** button of each step once, so it reads **I=0**. Then add six steps that run instead when **I=1**:

- step 3, I=1: `sp.dec`
- step 4, I=1: `sp.output` `mem.loadmar`
- step 5, I=1: `pc.output` `mem.load` `sp.dec`
- step 6, I=1: `sp.output` `mem.loadmar`
- step 7, I=1: `status.output` `mem.load`
- step 8, I=1: `vec.output` `pc.load` `pic.disable` `ir.reset`

This is `CALL` without an operand. Steps 3 to 5 push the program counter, which already points at the next instruction, and steps 6 and 7 push the flags, because the handler will change them. Step 8 jumps to the address in `vec` and switches interrupts off, so the handler cannot be interrupted by the request it is about to handle. After `ir.reset` the next fetch samples I again: interrupts are off, so I is 0, and the ordinary two steps fetch the handler's first instruction.

## Write SETV, EI, ACK and RTI

Four instructions, one for each job:

- `SETV` has one operand of type **address** and loads it into `vec`: `pc.output` `mem.loadmar`, then `mem.output` `vec.load` `pc.inc` `ir.reset`.
- `EI` switches interrupts on: `pic.enable` `ir.reset`.
- `ACK` clears the request: `pic.output` `pic.ack` `ir.reset`. The controller puts its pending bits on the bus and clears the bits it sees there, in one tick.
- `RTI` returns from the handler, `RET` with the flags popped first: `sp.output` `mem.loadmar`, then `mem.output` `status.load` `sp.inc`, then `sp.output` `mem.loadmar`, then `mem.output` `pc.load` `sp.inc` `pic.enable` `ir.reset`.

`RTI` switches interrupts back on in its last step, together with `ir.reset`, so the very next fetch samples I: a key that went down while the handler ran is taken straight away.

## Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
        SETV handler
        EI
main:   NOP
        JMP main

handler: KEYS
        LBI '0'
        ADD
        OUTA
        ACK
        RTI
```

The main loop does nothing but loop. It never reads the keypad. The handler reads the keys, prints the digit, as [Reading the keypad](exuarch:guide/reading-the-keypad) did, clears the request and returns.

## Run it

Open [Run](exuarch:tab/Run) and run it: the trace shows `NOP`, `JMP`, `NOP`, `JMP`. Tap a key on the keypad, or click **Use the keyboard** and press one: its digit appears. In the trace, the fetch after the tap ran its I=1 steps, then the handler, then `RTI`, and the main loop carried on. Watch `sp`: it goes to 65534 while the handler runs, two words pushed, and back to 0.

Hold a key down. It prints once. A key going down is one request, however long it stays down, where the polling program printed it every time round the loop. Press space: it prints `@`, 16 past `'0'`.

Things to try:

- Take `ACK` out of the handler. The request stays pending, and as soon as `RTI` switches interrupts on, the CPU is interrupted again: the handler runs for ever, and after the first digit it prints `0`, because `KEYS` now reads nothing pressed.
- Make the main loop count in `b` instead of doing nothing, and see what the handler's `LBI` does to it. Save `b` on the stack in the handler, and restore it before `RTI`.
- Add a **timer** as `irq1`, and a handler that prints a dot every so many ticks.

## Next

You have built a CPU with an accumulator, arithmetic, conditional jumps, subroutines, input and interrupts, from a bus and a clock. From here:

- [A stack machine](exuarch:guide/stack-machine): a new machine where every value lives on a stack.
- [Interrupts](exuarch:guide/interrupts) covers the controller, the timer, the real time clock and the I condition.
- [IRQ-16](exuarch:package/IRQ-16) runs a clock, a key counter and a drawing from one handler, and a game.
