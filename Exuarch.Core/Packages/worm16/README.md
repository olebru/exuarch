# WORM-16

A machine with no jumps. Every other computer here keeps its program in one place and moves a program counter around in it; WORM-16 keeps its program moving. The program is a queue of instructions crawling round a ring memory. The [head](exuarch:device/head) runs each instruction and eats it, and every instruction sets its own **fate**: whether a copy of it goes to the [tail](exuarch:device/tail), to run again on the next lap, whether it dies, or whether it divides. A loop is a few instructions that stay alive. Code that runs later, such as what comes after a loop, rides along inside an **egg** until it is time to hatch.

As far as we know, no computer has ever been built this way. Its nearest relatives are the imp in the programming game Core War, a one instruction program that copies itself forward through a ring of memory, and cyclic tag systems, a model of computation that works on a queue. In WORM-16 crawling is not a trick a program uses: it is the only way to run.

## The ring

[THE RING](exuarch:device/mem) is a memory of 4096 cells. Its addresses wrap, so cell 4096 is cell 0 again, and the worm can go round and round: the head and the tail count on forever, and the memory takes their addresses modulo its size. Programs start with `TAIL_D end`, which puts the tail just after the program, so the first copies land right behind the code that is running.

The [fetch](exuarch:instruction/FETCH) reads the opcode at the head, moves the head on, and keeps a copy of the opcode in [OPCODE](exuarch:device/t). An instruction that reads an operand keeps it in [OPERAND](exuarch:device/y). So when an instruction is kept, it can write itself to the tail from those two registers without reading memory again.

## Fates

Each instruction comes in four forms, one for each fate, written as a suffix:

| Suffix | Fate | Use |
|---|---|---|
| none | kept: it runs again every lap | the body of a loop that never ends by itself |
| `_D` | dies: it runs once and is not kept | setting up, such as [TAIL_D](exuarch:instruction/TAIL_D) and [LDA_D](exuarch:instruction/LDA_D) |
| `_P` | lives while N is clear: then it runs and is kept; once N is set it is skipped and dropped | the body of a loop that ends, such as [OUT_P](exuarch:instruction/OUT_P) |
| `_2` | divides: it runs, and two copies of it are kept | growing, such as [OUTC_2](exuarch:instruction/OUTC_2) |

The instructions themselves are few: `LDA`, `LDB`, `ADD`, `SUB`, `ADDB` and `SWAP` work on [A](exuarch:device/a) and [B](exuarch:device/b), `OUT` prints A as a character and `OUTC` prints a character on the [LCD](exuarch:device/lcd), [OUT2](exuarch:instruction/OUT2) `10, '0'` prints A as a number of one or two digits, `TAIL` sets the tail, and [HLT](exuarch:instruction/HLT) stops. `LDA`, `ADD`, `SUB` and `ADDB` set the flags.

## Gates and eggs

Gates and eggs control what lives, and they are what make loops end.

[GATE](exuarch:instruction/GATE) `v` opens every lap of a loop. It is kept while A >= v, and leaves N clear. When A < v the gate closes: it sets N and removes the copy of itself it has just written, so it is gone. With N set, every `_P` instruction after it is skipped, and is not kept either. The loop does not jump out; it dissolves. GATE suits a loop that counts down; [UPTO](exuarch:instruction/UPTO) `v` is the same gate for one that counts up, open while A <= v.

[HATCH](exuarch:instruction/HATCH) `n` is an egg: the n cells after it. While N is clear, HATCH copies itself and those n cells to the tail without running them, so the egg rides along lap after lap. When N is set, it hatches: the head runs on into the egg, and HATCH is not kept. HATCH leaves A and the flags unchanged.

So [Count down and go](<exuarch:program/Count down and go>) is one gate, two instructions that live while it is open, and an egg:

```
        GATE    '0'         ; while A >= '0', open the lap
        OUT_P               ; print A
        SUB_P   1
        HATCH   5           ; the 5 cells below ride along until the gate closes
        OUTC    'G'
        OUTC    'O'
        HLT
```

Each lap prints a digit and counts A down. When A drops below `'0'`, the gate closes, the loop's body falls away, the egg hatches, and the program prints `GO` and stops. The whole run prints `9876543210GO` in 712 ticks.

## A loop inside the microcode

Carrying an egg of n cells takes n trips through the same steps, and microcode only runs forwards. HATCH loops anyway, the way a real microprogrammed CPU does with a sequencer: it jumps inside its own microcode by loading the [instruction register](exuarch:device/ir) itself.

The instruction register holds the address of the micro step that runs next, and an opcode is just the address of an instruction's first step. So while HATCH sets up, it copies its opcode from OPCODE into [HATCH LOOP](exuarch:device/j) and counts it up once in each of its first nine steps, until it holds the address of step 9, where the loop starts. Each trip carries one cell and counts A down; while A is not 0, the last step is `j.output ir.load`, back to step 9. A saved copy of A in [SAVED A](exuarch:device/k) and of the flags in [SAVED FLAGS](exuarch:device/s) puts everything back when the loop is done. Open [Microcode](exuarch:tab/Microcode) and look at HATCH to see the whole loop.

## Printing a number without a loop

There is no divide, so OUT2 works out the tens by taking 10 off A for as long as it can, counting in [TENS](exuarch:device/d), which starts at the zero character from the operand, kept in [ZERO](exuarch:device/z). Rather than loop, it does it nine times in a row: each time it compares, and only when A is still at least 10 does the next step take 10 off. For when it is not, that step has a twin that does nothing, so the microcode is equally long whatever the flags are, and the steps after the nine line up. Then a compare of the original A with 10 selects whether there is a tens digit to print, the units go out as A plus the zero character, and A and the flags are put back. That is why OUT2 stops at 99: a third digit would take another nine steps and another register, and is left for you to add.

## Things to try

1. Run [Count down and go](<exuarch:program/Count down and go>) one tick at a time with the **Memory** panel open. The PC mark is the head, and the cells marked as written are the tail. The program crawls forward a few cells every lap, and behind it the old copies are left to be written over.
2. [Cell division](<exuarch:program/Cell division>) prints `* ** **** ********`: `OUTC_2 '*'` goes to the tail twice, so every lap has twice as many stars, and the worm grows. Take out `HATCH 1` and `HLT` and it grows until its tail runs into its head, after which it starts writing over code it has not run yet: the worm eats itself.
3. [Fibonacci](<exuarch:program/Fibonacci>) prints `0 1 1 2 3 5 8 13 21 34 55 89`. A and B hold two numbers of the sequence in a row; every lap prints A with `OUT2 10, '0'`, adds the two with `ADDB`, and moves them along with `SWAP`. `UPTO 89` closes when the next number would need three digits. Its body is made of plain kept instructions, not `_P` ones: the egg right after the gate stops the machine in the lap the gate closes, so the body never runs again anyway.
4. [Two phases](<exuarch:program/Two phases>) prints `54321 EDCBA`. The second loop, with its own gate and egg, rides inside the first loop's egg, so an egg can hold an egg. When the first gate closes, the egg hatches, `LDA_D 'E'` clears N, and the second loop runs on its own.
5. Print the alphabet from `A` to `Z` with `UPTO`, and then backwards with `GATE`.
6. Fibonacci is also the story of rabbits, a pair of which has a new pair every month once it is grown up. Make the worm breed: a fate for a young instruction that is kept as a grown up one, and one for a grown up that is kept and has a young one too. Count the instructions in every lap.
7. Add a fate of your own, such as `_Z`, kept while Z is set, or `_3`, which divides into three. Each is a copy of an existing instruction in [Microcode](exuarch:tab/Microcode) with its fate steps changed.
8. Compare it with [CISC-16](exuarch:package/CISC-16)'s *Code is data*, which rewrites its own instructions in place. Here the program rewrites where it is, all the time.

## Read more

- [Memory and banks](exuarch:guide/memory-and-banks): why an address wraps at the size of a memory.
- [Flags and conditions](exuarch:guide/flags-and-conditions): steps that run only for some flags, which is how the fates are made.
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register): opcodes as micro step addresses, which HATCH's loop uses.
- [ExµArch and real hardware](exuarch:guide/real-hardware): everything here could be built from real parts.
