# RISC-16

A load/store machine in the style of RISC processors such as ARM and RISC-V. Its eight registers are held in a **register file**, every arithmetic instruction names three of them, and memory is only reached by loads and stores.

## The idea

RISC designs trade a rich instruction set for a regular one, and RISC-16 keeps to three rules:

- **Every register works everywhere.** The register an instruction uses is an operand, `R0` to `R7`, so `ADD`, `LDR` and `OUT` each work with any of them. The assembler turns `R5` into the number 5 in the operand cell, and the microcode moves that number into the register file's select latch.
- **Three registers per operation.** `ADD R0, R1, R2` puts R1 + R2 into R0 and leaves R1 and R2 alone, so a value can be used again without copying it first. `ADDI R1, R1, 1` is how a register counts.
- **Only loads and stores touch memory.** [LDR](exuarch:instruction/LDR) `Rd, Ra` reads the cell whose address is in Ra, and [STR](exuarch:instruction/STR) `Rs, Ra` writes one. Nothing else reaches memory, not even a call.

## The parts

- [The register file](exuarch:device/rf) holds R0 to R7. `select` latches a register number from the bus, and `output` and `load` then work on the selected register.
- [DESTINATION](exuarch:device/d) holds the first register number while the instruction selects the others.
- [ALU A](exuarch:device/x) and [ALU B](exuarch:device/y) hold the ALU's two inputs, since neither can be wired to one fixed register.
- [FLAGS](exuarch:device/flags) holds the N, V, C and Z flags that the branches test.
- [The LCD](exuarch:device/lcd), [the screen](exuarch:device/fb) and [the keypad](exuarch:device/keys) are reached with `OUT`, `PX`, `PY`, `PLOT` and `IN`, each taking a register.

## How an instruction runs

[ADD](exuarch:instruction/ADD) `Rd, Rs, Rt` reads its three operand cells one after the other. Rd is held in DESTINATION, Rs is selected and copied into ALU A, Rt is selected and copied into ALU B, then Rd is selected again and the ALU drives the sum straight into it, setting the flags. There is one bus, so every transfer is a tick of its own: 12 ticks with fetch, six of them to read the three operand cells. `ADDI Rd, Rs, value` takes 11, `MOV` and `LDR` take 9, `MOVI` takes 6.

## Calls without a stack

[BL](exuarch:instruction/BL) `R7, label` puts the address after the call into R7 and jumps; [BX](exuarch:instruction/BX) `R7` jumps back to it. This is RISC-V's `JAL` and ARM's link register. A call costs 8 ticks and touches no memory, which is why RISC calls are cheap.

The register is an operand, so any register can hold the link, and by convention R7 does. A subroutine that calls another has to save R7 first. RISC-16 has no `PUSH` or `POP`: by convention R6 is the stack pointer, and saving a register is a subtraction and a store:

```asm
        SUBI   R6, R6, 1     ; make room
        STR    R7, R6        ; save the link
        BL     R7, inner
        LDR    R7, R6        ; get it back
        ADDI   R6, R6, 1
        BX     R7
```

Every register starts at 0, so the first `SUBI` takes R6 to 65535, and a memory address wraps round to the top of memory.

## Things to try

1. [Hello, world on the LCD](<exuarch:program/Hello, world on the LCD>) walks the string with `LDR R0, R1` and `ADDI R1, R1, 1`.
2. [Fibonacci on the LCD](<exuarch:program/Fibonacci on the LCD>) makes each term with `ADD R0, R1, R2` and stops when it carries out of 16 bits. Its print routine keeps to R0 and R3 to R5, so a and b survive every call.
3. [Sketch with the arrow keys](<exuarch:program/Sketch with the arrow keys>) reads the keypad once with `IN R0` and tests each key with `ANDI R4, R0, 1`, which leaves R0 as it is. In the [Run view](exuarch:tab/Run), switch on ⚡ Max, click the keypad's *Use the keyboard* and draw.
4. [Colour gradient on the screen](<exuarch:program/Colour gradient on the screen>) counts the colour up in place with `ADDI R3, R3, 0x0800` and stops a row when it carries.
5. Make the print routine of the Fibonacci program call a second routine to print each character, and save R7 on the stack as above.

## Read more

- [Operands](exuarch:guide/operands): value, address and register operands.
- [Flags and conditions](exuarch:guide/flags-and-conditions): the flags the branches test.
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack): calls through a stack in memory, to compare with the link register.
