# TINY-16

The machine to start with. It has one register you work with, **A**, and seven instructions. That is too few for most programs, but it is enough to see what assembly is: a list of small steps that the CPU carries out one after the other.

## The instructions

| Instruction | What it does | Opcode |
|---|---|---|
| [LOAD](exuarch:instruction/LOAD) `n` | put the number `n` into A | 2 |
| [ADD](exuarch:instruction/ADD) `n` | A = A + `n` | 4 |
| [SUB](exuarch:instruction/SUB) `n` | A = A - `n` | 7 |
| [OUT](exuarch:instruction/OUT) | print A on the LCD as a character | 10 |
| [JUMP](exuarch:instruction/JUMP) `label` | go on from `label` | 11 |
| [JZ](exuarch:instruction/JZ) `label` | go on from `label` if the last ADD or SUB gave 0 | 13 |
| [HALT](exuarch:instruction/HALT) | stop | 15 |

A line of a program is an instruction, sometimes with a number after it. `'H'` is a character literal: it means the character code of H, 72. A word followed by a colon, like `loop:`, is a label, a name for the address of the line it is on, so `JUMP loop` can go back to it.

## What is in memory

The assembler turns each instruction into its opcode, and each number after it into one more memory cell. `LOAD 'H'` becomes the two cells `2 72`, and `OUT` becomes `10`. So [Say Hi](<exuarch:program/Say Hi>) is just these seven numbers in [memory](exuarch:device/mem):

```
2  72    10    2  105    10    15
LOAD 'H'  OUT  LOAD 'i'  OUT   HALT
```

There is nothing else: the program is numbers, and the CPU reads them one at a time.

Why 2, 10 and 15, and not 1, 2 and 3? An opcode is the address of the instruction's first step in the decoder's ROM, the table of steps you see in [Microcode](exuarch:tab/Microcode). Fetch has steps 0 and 1, LOAD's two steps come next at 2 and 3, ADD's three at 4 to 6, and so on. JZ needs only two rows, 13 and 14: its taken and not taken steps share them, and the Z flag selects which one runs.

## The parts

- [PC](exuarch:device/pc), the program counter, holds the address of the next cell to read.
- [MEMORY](exuarch:device/mem) holds the program, 256 cells.
- [IR](exuarch:device/ir), the instruction register, holds the opcode being run.
- [A](exuarch:device/a) is the register the instructions work on.
- [The ALU](exuarch:device/alu) adds and subtracts. It adds A and [OPERAND](exuarch:device/b), which holds the number from the program, and writes the result back into A.
- [FLAGS](exuarch:device/flags) holds whether the last result was 0, the Z flag that JZ tests.
- [The LCD](exuarch:device/lcd) prints characters.
- [The clock](exuarch:device/clk) ticks until HALT stops it.

## How an instruction runs

Every instruction starts the same way, with [fetch](exuarch:tab/Microcode): the PC's address goes to memory, the opcode at that address goes into IR, and the PC steps on. The opcode then selects the instruction's own steps. [LOAD](exuarch:instruction/LOAD) has two: the PC's address goes to memory again, and the number in that cell goes into A.

All parts share one bus, and each step is one tick of the clock: one part puts a value on the bus and another takes it. So `LOAD 'H'` takes four ticks, two for fetch and two of its own, and `OUT` takes three.

## Things to try

1. Open [Say Hi](<exuarch:program/Say Hi>), go to [Run](exuarch:tab/Run) and press **Tick** (→) one tick at a time. Watch the PC count up, the value move over the bus and A change, and read the **Trace**.
2. [2 + 3](<exuarch:program/2 + 3>) shows that characters are numbers: `'2'` is 50, and adding 3 gives 53, the code of `'5'`.
3. [Count down from 9 to 0](<exuarch:program/Count down from 9 to 0>) is a loop. `JUMP loop` goes back, and `JZ done` leaves when A is `'0'`. Follow the PC in the trace to see it jump.
4. Change Say Hi to print your name.
5. Add an instruction of your own in [Microcode](exuarch:tab/Microcode): `DEC`, A = A - 1, needs no number after it.

## Next

When this makes sense, [BYOC-16](exuarch:package/BYOC-16) has more registers, memory you can read and write, a stack and subroutines.

- [Your first machine](exuarch:guide/first-machine): build a machine like this one yourself.
- [Assembly](exuarch:guide/assembly): labels, numbers, characters and strings.
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register): how an opcode selects its steps.
