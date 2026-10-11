# STACK-16

A machine with no registers for the program to name. Every instruction takes its operands from the top of a **data stack** and leaves its result there, so [ADD](exuarch:instruction/ADD) has no operands at all: it pops two values and pushes their sum. Calls have a stack of their own, the **return stack**, so a subroutine can never pop a number as a return address.

This is how Forth works, how the Java and .NET virtual machines run their bytecode, and how pocket calculators with an **ENTER** key worked.

## The idea

A register machine has to name where everything is: `ADD R0, R2` names two registers. A stack machine names nothing, because the operands are always in the same place, on top of the stack. Programs come out in reverse Polish notation: `(9 - 4) + (1 + 2)` is `9 4 - 1 2 + +`, or in STACK-16's assembly:

```asm
        PUSH  9
        PUSH  4
        SUB               ; 5
        PUSH  1
        PUSH  2
        ADD               ; 3, on top of the 5
        ADD               ; 8
```

The price is that values have to be shuffled into place. [DUP](exuarch:instruction/DUP) copies the top value, [OVER](exuarch:instruction/OVER) copies the one below it, [SWAP](exuarch:instruction/SWAP) swaps the top two and [POP](exuarch:instruction/POP) drops the top one.

## The parts

- [The data stack pointer](exuarch:device/sp) holds the address of the value on top of the data stack, which sits at the top of [memory](exuarch:device/mem). The stack is full descending: a push first counts the pointer down and then writes. It starts at 0, so the first push lands in 65535, which the 4096 cell memory reads as its last cell, 4095.
- [ALU A](exuarch:device/x) and [ALU B](exuarch:device/y) hold the two values [the ALU](exuarch:device/alu) works on, popped off the stack.
- [The return stack](exuarch:device/rmem) is a memory of its own, 64 cells, with its own [pointer](exuarch:device/rp). [CALL](exuarch:instruction/CALL) pushes the address after the call there, and [RET](exuarch:instruction/RET) pops it into the program counter.
- [FLAGS](exuarch:device/flags) is only used inside an instruction: [JZ](exuarch:instruction/JZ) and [JN](exuarch:instruction/JN) pop a value, compare it with 0 and jump on the result.

## How an instruction runs

[PUSH](exuarch:instruction/PUSH) takes 6 ticks with fetch. Its first step counts the stack pointer down while the program counter addresses the operand, then the operand goes into ALU A, the stack pointer addresses the new top, and ALU A is written there.

[ADD](exuarch:instruction/ADD) takes 7. It reads the top value into ALU B and moves the stack pointer up, reads the next one into ALU A, and lets the ALU write the sum straight back into that cell, which is now the top. Two values in, one out, and the stack is one shorter.

[LOAD](exuarch:instruction/LOAD) and [STORE](exuarch:instruction/STORE) reach the rest of memory: [PUSHA](exuarch:instruction/PUSHA) pushes a label's address, `LOAD` replaces it with the value stored there, and `value address STORE` writes.

## Things to try

1. Step [Work out an expression](<exuarch:program/Work out an expression>) with the **Memory** tab open, and go to the end of memory: the stack grows down from cell 4095 and shrinks back as each `ADD` and `SUB` folds two values into one. The whole program takes 65 ticks and prints `8`.
2. [Count down from 9](<exuarch:program/Count down from 9>) keeps its counter on the stack. Take out the first `DUP` and work out what goes wrong before you run it.
3. [The sum of 1 to 100](<exuarch:program/The sum of 1 to 100>) prints `5050` with a print routine that calls itself once for every digit in front. Watch [the return stack pointer](exuarch:device/rp) count down four times, once for the call from the main program and once for each digit in front, and back up again while the digits stay on the data stack.
4. Write `MUL` from `ADD` and a loop, as a subroutine: `a b MUL` should leave `a * b`.

## Read more

- [A stack machine](exuarch:guide/stack-machine): a tutorial that builds `PUSH`, `ADD` and `OUT` on the minimal CPU.
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack): a stack used for return addresses only.
- [Memory and banks](exuarch:guide/memory-and-banks): how a memory addresses its cells.
