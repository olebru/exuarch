# CISC-16

[RISC-16](exuarch:package/RISC-16)'s opposite. RISC-16 only reaches memory through `LDR` and `STR`, and does arithmetic in registers. CISC-16 lets almost every instruction reach memory, in any of six ways, and does as much work per instruction as it can: one `MOV_PP` copies a character from one string to another and steps both pointers on. This is how the VAX, the 68000 and x86 were built, and why they were.

## The idea

An operand specifies not only what, but where. CISC-16 has six **addressing modes**. Because the assembler has no brackets, the modes are part of the mnemonic: one letter for the destination, then one for the source.

| Letter | Mode | Operands | Means |
|---|---|---|---|
| `I` | immediate | a value | the number itself (sources only) |
| `R` | register | Rn | the register |
| `A` | absolute | a label | the cell at that address |
| `N` | register indirect | Rn | the cell Rn points at, `[Rn]` |
| `X` | indexed | Rn, d | the cell at Rn + d, `[Rn + d]`; d can be negative |
| `P` | post-increment | Rn | `[Rn]`, then Rn steps on by one |

So `ADD_RX R0, R6, -1` is R0 = R0 + [R6 - 1], and `MOV_AR total, R0` stores R0 at `total`. [MOV](exuarch:instruction/MOV_RR), [ADD](exuarch:instruction/ADD_RR), [SUB](exuarch:instruction/SUB_RR), [AND](exuarch:instruction/AND_RR) and [CMP](exuarch:instruction/CMP_RR) come in every pair of modes, 30 each. [PUSH](exuarch:instruction/PUSH_R) and [OUT](exuarch:instruction/OUT_R) take any source mode, and [POP](exuarch:instruction/POP_R) any destination. [LEA](exuarch:instruction/LEA) `Rd, label` loads an address rather than what is stored there, and [LEAX](exuarch:instruction/LEAX) `Rd, Rb, d` works one out.

The price is in the decoder. RISC-16 has 37 instructions; CISC-16 has 184, in 1,692 micro step addresses, because every combination of modes is its own microcode.

## The parts

- [The register file](exuarch:device/rf) holds R0 to R7, and [SP](exuarch:device/sp) points at the top of the stack, which grows down from the top of [memory](exuarch:device/mem).
- [EFFECTIVE ADDRESS](exuarch:device/ea) holds the address of a memory destination while the source is fetched, and [DESTINATION](exuarch:device/d) the number of a register destination.
- [The ALU](exuarch:device/alu) works on [ALU A](exuarch:device/x) and [ALU B](exuarch:device/y) and sets [FLAGS](exuarch:device/flags).
- [The address unit](exuarch:device/agu) is a second ALU, with its own inputs and its own flags, that only adds addresses: Rn + d for indexed operands, and the stack pointer for `ENTER` and `FREE`. Working out an address therefore never changes the flags a branch is about to test, which real CISC processors also take care of.

## How an instruction runs

An instruction addresses its destination, reads it, addresses and reads its source, and writes the result back. With fetch, `ADD_RR R0, R1` takes 10 ticks. `ADD_RX R0, R1, 2` takes 14, because the address unit first adds R1 and 2. `ADD_XX R0, 1, R1, 2` takes 19, with two addresses to work out and a memory cell to read and write. RISC-16's `ADD R0, R1, R2` takes 12, but a program needs extra `LDR` and `STR` instructions around it to do the same work on memory. `MOV_PP R2, R1` takes 11 for a load, a store and two increments.

## Stack frames

A function that calls itself needs its own copy of its arguments and local variables at every level. [ENTER](exuarch:instruction/ENTER) `R6, n` builds a **stack frame**: it pushes the caller's frame pointer, points R6 at the new frame and makes room for n locals below it. [LEAVE](exuarch:instruction/LEAVE) `R6` takes it down again. With the argument pushed by the caller and the return address pushed by [CALL](exuarch:instruction/CALL), a frame reads:

| Where | What |
|---|---|
| `[R6 + 2]` | the argument |
| `[R6 + 1]` | the return address |
| `[R6]` | the caller's R6 |
| `[R6 - 1]` | the local |

After the call returns, [FREE](exuarch:instruction/FREE) `1` drops the argument. This is the frame x86 builds with `ENTER`, `LEAVE` and EBP.

## Things to try

1. [Sum a table](<exuarch:program/Sum a table>) adds the table with `ADD_RP R0, R1`, one instruction per value, prints `150`, and stores the sum at `total` with an absolute destination.
2. [Copy a string](<exuarch:program/Copy a string>) copies with `MOV_PP R2, R1`, checks the character it just copied with `CMP_XI R2, -1, 0`, and prints the copy, `Copied by MOV_PP`, with `OUT_P R1`.
3. Step [Recursive Fibonacci](<exuarch:program/Recursive Fibonacci>) with the **Memory** tab on the last page and watch the frames stack up. It prints `0 1 1 2 3 5 8 13 21 34 55` in 49,831 ticks, and while working out fib(10) the stack reaches 40 cells deep: ten frames of four.
4. [Code is data](<exuarch:program/Code is data>) shows what makes this a von Neumann machine: the program and its data share one memory and one bus, so an instruction is just cells that other instructions can change. `OUT_I 'A'` prints its own operand, and `ADD_XI R1, 1, 1` adds one to that operand, so the same instruction prints `ABCDEFGHIJKLMNOPQRSTUVWXYZ`. Then `MOV_AA start, stop` copies a whole `HLT` over the program's first instruction, and the jump back stops on it, after 838 ticks. Watch the **Memory** panel: the operand cell counts up while the **Program** listing still shows `'A'`, because the listing is the source and memory is what runs. [HARVARD-16](exuarch:package/HARVARD-16) can not do this, since its data instructions only reach its data memory. It is also why bugs that write over code are possible here, and why modern systems mark code read only.
5. Write the sum on RISC-16, and count the instructions and the ticks.
6. Add an `M` mode, pre-decrement, `[Rn]` after Rn steps back by one, and use it to fill a table backwards.

## Read more

- [RISC-16](exuarch:package/RISC-16), the other way to build a machine.
- [STACK-16](exuarch:package/STACK-16), where every value is kept on a stack.
- [Operands](exuarch:guide/operands): value, address and register operands.
- [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack): calls and returns through a stack.
