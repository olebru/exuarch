# Operands

Most instructions need something to work on: a number to load, an address to read, a place to jump to. That something is an operand, stored in memory right after the opcode. The microcode determines how an instruction reads its operands; the instruction's definition declares how many there are and what they mean, and the assembler and the editor use that to check your program and annotate it.

## Operands in memory

Every opcode and every operand takes one 16 bit cell. `LAI 42` assembles to two cells, the opcode of `LAI` and then 42; an instruction with two operands takes three cells. Nothing in the hardware encodes this. It is the microcode that steps the program counter over the operand cells and reads them, and the definition that makes the assembler check for them.

## Count and types

Each instruction in the [Microcode](exuarch:tab/Microcode) tab has an **Operands** count and, when the count is above 0, an **Operand types** choice per operand:

- **value**: used as it is, like the number `LAI` loads.
- **address**: a memory location to read, write or jump to, like the target of `JMP`.
- **register**: a register of the machine's [register file](exuarch:reference/registerFile), written `R0`, `R1` and so on. See *Register operands* below.

In the machine's JSON these are `operands` and `operandTypes`:

```json
{ "mnemonic": "LDA", "operands": 1, "operandTypes": ["address"], "steps": [ ... ] }
```

Both are optional. Without `operands` the count is taken from the number of `operandTypes`; with neither, the assembler does not check how many operands a line has. When both are given they must match, or the microcode editor reports an error.

The types never change what the assembler produces. They change what it checks and what the editor shows you: hovering over `LDA` in the program editor shows `LDA address`, and hovering over an operand shows whether it is used as an address or as a value.

## What the assembler accepts

Operands follow the mnemonic, separated by commas. Each one is one of:

- a number, `42` or `0x2A`, from 0 to 65535;
- a character, `'A'`, which is its Latin-1 code;
- a label, which is the address the label marks;
- a register, `R3`, but only where the instruction declares a register operand.

A leading `#`, as in `#42`, is accepted and means the same. A string is not an operand; strings only go in `.DATA` and `.STRING` lines.

The assembler reports an error when a line has a different number of operands than the instruction declares, when a number is out of range, and when a label is not defined.

## Labels as values

A label is always an address. That is exactly right for an address operand, `JMP loop`, but usually a mistake for a value operand: `LAI msg` loads the address of `msg`, not what is stored there. So when an instruction declares a value operand and you give it a label, the assembler warns:

```
LAI takes a value here, and 'msg' is the address of a label (0x0012). Use a number, or an instruction that takes an address.
```

It is a warning, not an error, because sometimes the address is what you want, for example to load a pointer into a register. [RISC-16](exuarch:package/RISC-16)'s `ADR Rd, label` takes an address operand for exactly that reason. The warning only appears when the instruction declares its operand types.

## Register operands

There are two ways to give a machine registers, and ExµArch supports both.

**Registers named in the microcode.** Each register is a device of its own, and the instruction's microcode names it, so the register is part of the mnemonic. [BYOC-16](exuarch:package/BYOC-16) works this way: `LAI` loads A and `LBI` loads B. The hardware is as plain as it gets, but every register needs instructions of its own.

**Registers as operands.** A [register file](exuarch:reference/registerFile) holds several registers in one device, 8 unless its `count` is set otherwise, and works on the one its `select` line last latched from the bus. The register number is then an operand like any other: the instruction declares a **register** operand, the program writes `R0` to `R7`, and the assembler puts the number in the operand cell. [RISC-16](exuarch:package/RISC-16) works this way: `MOV R1, R0`, `ADD R0, R1, R2`, `OUT R3`.

In microcode a register operand is read like a value, straight into the register file's select latch:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, pc.inc, rf.select
step 3: rf.output, lcd.load, ir.reset
```

That is RISC-16's `OUT Rs`; its fetch has already stepped the program counter onto the operand. An instruction with two register operands keeps the first number in a plain register while it selects the second, and moves it back into the select latch when it uses the first register again.

The assembler takes `R` or `r` followed by a number below the register file's count, and only in a register slot. A label in a register slot is an error, and so is `R8` on a machine with eight registers. In every other slot, `R1` is an ordinary label name. An instruction that declares a register operand on a machine without a register file is a microcode error.

## Reading operands in microcode

An instruction reads an operand like fetch reads an opcode: the program counter goes on the bus as an address, the cell comes back on the bus, and the program counter moves on. [BYOC-16](exuarch:package/BYOC-16)'s fetch leaves the program counter on the opcode, so its `LAI` starts by stepping onto the operand:

```
step 1: pc.inc
step 2: pc.output, mem.loadmar
step 3: mem.output, rega.load, regi.reset, pc.inc
```

For a value operand the cell itself is the result, loaded straight into A in step 3.

For an address operand the cell is used as an address, which costs another tick. `LDA` reads the operand into the data memory's address register and only then reads the data:

```
step 1: pc.inc
step 2: pc.output, mem.loadmar
step 3: mem.output, mmu.loadmar
step 4: rega.load, mmu.output, regi.reset, pc.inc
```

An instruction with several operands repeats the read for each one, stepping the program counter after every cell, and must leave the program counter on the next opcode before it returns to fetch. If an instruction reads fewer operand cells than it declares, the rest will be fetched as if they were opcodes.

## See also

- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [Assembly](exuarch:guide/assembly)
- [Registers and the ALU](exuarch:guide/registers-and-the-alu)
- [Microcode](exuarch:guide/microcode)
- [Memory and banks](exuarch:guide/memory-and-banks)
