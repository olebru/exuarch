# Registers and the ALU

Watch this tutorial as a video: [episode 6 of Build a computer in ExµArch](https://www.youtube.com/watch?v=zWaQvaRvTzk) on YouTube.

The machine from [Your first machine](exuarch:guide/first-machine) can print what the program spells out, but it cannot compute anything. In this tutorial you give it two registers and an ALU, and it counts along the alphabet: `A`, `B`, `C`.

Skipped a tutorial, or lost your machine? Load [Tutorial 6 · Registers and the ALU](<exuarch:package/Tutorial 6 · Registers and the ALU>): it is the machine this tutorial starts from.

## Add two registers

1. Open [Hardware design](<exuarch:tab/Hardware design>).
2. Drag two **register** devices onto the canvas next to the bus, or click **register** in the palette twice.
3. Set their IDs to `a` and `b`.

A [register](exuarch:reference/register) holds one 16 bit value between ticks. `output` drives it onto the bus, `load` stores the bus value, and `reset`, `inc` and `dec` change it in place. Register `a` will be the accumulator, where results end up; `b` holds the second operand.

## Find the status register

The ALU needs a status register to write its flags into, and the decoder reads the same register to choose which steps run. The minimal CPU already has one:

1. Find the device `status` on the canvas. It is a [statusRegister](exuarch:reference/statusRegister) on the bus `main`.
2. Find the card `decoder`. The decoder runs the microcode; it is not on a bus, and its sockets name the devices it works with. Its **status** socket is wired to `status`, and when you select the decoder the inspector shows **Status register** is set to `status`.

If you started from **Empty**, the machine has neither: add a decoder first, from the palette's **Decoder** group, then drag a **statusRegister** onto the bus (it gets the ID `status`) and drag the decoder's **status** socket onto it.

## Add an ALU

1. Drag an **alu** onto the canvas, or click it in the palette. Its ID is already `alu`.
2. In the inspector, under **Connections**, set **a** to `a`, **b** to `b` and **status** to `status`.

The [alu](exuarch:reference/alu) is not wired to its operands through the bus: it reads the two registers you connect as `a` and `b` directly, and writes its flags straight into the connected status register. What it puts on the bus is the result. `alu.add` drives `a + b`, `alu.sub` drives `a - b`, and there are lines for `and`, `orr`, `eor` and the shifts `lsl` and `lsr`.

## Write four instructions

Open [Microcode](exuarch:tab/Microcode) and add these, with **＋ Instruction** for each.

`LAI` loads `a` with a value, one operand of type **value**:

- step 1: `pc.output` `mem.loadmar`
- step 2: `mem.output` `a.load` `pc.inc` `ir.reset`

`LBI` is the same for `b`, one **value** operand:

- step 1: `pc.output` `mem.loadmar`
- step 2: `mem.output` `b.load` `pc.inc` `ir.reset`

`ADD` has no operands and one step:

- step 1: `alu.add` `a.load` `ir.reset`

`OUTA` prints `a`, no operands:

- step 1: `a.output` `lcd.load` `ir.reset`

`LAI` and `LBI` are `OUT` from the last tutorial with a register in place of the display. Duplicate `OUT` and change the signals if you like.

`ADD` does its work in one tick. The ALU reads `a` and `b` in the first half of the tick and drives the sum onto the bus; in the second half `a` stores the sum. `a` changes only at the end of the tick, so reading and writing it in the same step is safe. This is also why `ADD` needs no `pc.inc`: fetch has already stepped past its opcode, and it has no operand to skip.

## Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
        LAI 'A'
        OUTA
        LBI 1
        ADD
        OUTA
        ADD
        OUTA
        HLT
```

## Run it

Open [Run](exuarch:tab/Run) and run it: the LCD shows `ABC`. `a` starts at 65, the code of `A`, and each `ADD` adds the 1 in `b`.

Watch the **Trace** during an `ADD`: its bus column shows the ALU driving the sum and `a` taking it, in one tick. The ALU also wrote the status register; it does that on every `alu` operation, which the next tutorial uses.

## Forget a.load

`ADD`'s one step has two parts: the ALU drives the sum, and `a` stores it. What if `a` does not?

1. Remove `a.load` from `ADD`, reset and run.

The LCD shows `AAA`. Open the **Trace** at an `ADD`: the ALU still drives the sum onto the bus, `66`, but no device reads it. A value on a bus lasts for one tick only; unless a register loads it in that tick, it is gone. `a` keeps its `A`, and every `OUTA` prints it again.

2. Put `a.load` back.

Things to try:

- Print `ACE` by loading `b` with 2.
- Add `SUB` with the step `alu.sub` `a.load` `ir.reset` and count backwards.

## Next

- [Loops and flags](exuarch:guide/loops-and-flags): make the machine decide what to do next.
- [Devices and control lines](exuarch:guide/devices-and-control-lines) covers inputs, outputs and connections.
- [Microcode](exuarch:guide/microcode) covers steps and signals in depth.
