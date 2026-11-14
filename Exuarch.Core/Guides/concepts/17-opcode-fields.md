# Opcodes and opcode fields

An opcode is the number in a program that says which instruction comes next. The decoder turns it into the address of that instruction's first step in the decoder ROM, and from there the steps run one per tick. ExµArch has two ways to turn an opcode into that address, and a machine picks one in its microcode:

- **Whole cells**: the opcode is the step address itself and fills a whole cell. Every operand takes a cell of its own after it. This is the simplest decoder there is, and the one [Opcodes are addresses](exuarch:guide/opcodes-are-addresses) builds.
- **Opcode fields**: the top bits of a cell are the opcode, and the bits below it hold **fields**: register numbers and small numbers that travel inside the same word. This is how real instruction sets are encoded.

The first half of this page is how each one works. The second half is how to design an instruction word with fields: how many bits to give the opcode, how wide to make each field, and what that decides.

## How whole cells work

The decoder ROM is addressed by two things: the **step counter**, the instruction register `ir`, and the five **status bits**, N, V, C and Z from the flags and I from the interrupt controller. The counter gives the low 16 bits of the address and the status bits sit above them, so each step address has 32 variants, one for each combination of the status bits. That is how a branch has different steps for `Z=1` and `Z=0`. See [Flags and conditions](exuarch:guide/flags-and-conditions).

The ROM is filled in the order of the instruction list. Fetch starts at step 0, and every instruction gets a **block** of step addresses right after the one before it, as long as its longest variant. With a two step fetch, a one step `HLT` and a three step `JMP`, fetch has addresses 0 and 1, `HLT` has 2 and `JMP` has 3, 4 and 5.

The opcode of an instruction is the first address of its block, so `HLT` is 2 and `JMP` is 3. When the assembler reads `HLT` it writes 2 into the cell. Fetch reads the cell and `ir.load` puts it in the counter, so the next tick runs step 2. Every tick without `ir.load` or `ir.reset` the counter counts up by one, which is how an instruction walks through its block, and the last step's `ir.reset` sends it back to fetch at 0.

Opcodes follow from the order and the length of the instructions. Add a step to `HLT` and `JMP` moves to 4. The meter in the Microcode toolbar counts the step addresses in use, out of the 65536 a 16 bit counter can reach.

## How opcode fields work

With `"opcodeBits": 6` in the microcode, the top 6 bits of a cell are the opcode, and the other 10 bits are free for fields. An opcode is now the instruction's place in the list, not its step address: fetch is opcode 0, the first instruction after fetch is 1, the next 2, and so on. 6 bits count 64 opcodes, and fetch takes one, which leaves room for 63 instructions.

The counter still needs a step address, so fetch loads the word into two devices at once:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, ir.load, iw.load, pc.inc
```

`ir.load` no longer takes the word as it is. It goes through a **mapping ROM**: the opcode bits select an entry, and the entry is the first step address of that instruction's block. Opcode 0, and every opcode no instruction has, maps to step 0, so an empty cell runs fetch again. The blocks themselves are laid out exactly as with whole cells.

The [instructionWord](exuarch:reference/instructionWord) device keeps the whole word for the rest of the instruction, and its field lines put one field at a time on a bus. It has two ports: `data`, where it loads the word, and `fields`, where its field lines drive. On a machine with one bus both are the same bus.

## Designing an instruction word

### The bit budget

A cell has 16 bits, and the opcode and the fields share them. Every bit given to the opcode doubles the number of instructions; every bit given to a field doubles what that field can hold.

| Opcode bits | Instructions | Bits left for fields | An example layout |
|---|---|---|---|
| 4 | 15 | 12 | three 4 bit register fields: 16 registers |
| 5 | 31 | 11 | two 4 bit fields and one 3 bit field |
| 6 | 63 | 10 | three 3 bit register fields: 8 registers, plus a 4 bit field for shift counts |
| 8 | 255 | 8 | two 4 bit fields, or one 8 bit number |

[FIELD-16](exuarch:package/FIELD-16) and [PORT-16](exuarch:package/PORT-16) use the 6 bit row:

```json
"opcodeBits": 6,
"fields": [
  { "low": 7, "bits": 3 },
  { "low": 4, "bits": 3 },
  { "low": 1, "bits": 3 },
  { "low": 0, "bits": 4 }
]
```

Field 0 is bits 9 to 7, field 1 bits 6 to 4, field 2 bits 3 to 1, and field 3 bits 3 to 0. A machine has at most four fields, because the instruction word has field lines for four. Every field lies below the opcode bits and is at least one bit wide.

### Register fields and the register file

A register operand in a field is just a number: the field's value is the register number. So the width of the field sets how many registers an instruction can name. A 3 bit field holds 0 to 7 and names R0 to R7; a 4 bit field holds 0 to 15 and names R0 to R15.

The [multiport register file](exuarch:reference/multiportRegisterFile) has two kinds of parameters for this, and they count different things:

- `count` is how many registers the file has, from 2 to 16.
- `readA`, `readB`, `write` and `write2` say **which field** holds the register number for each port. They take 0 to 3 because there are four fields, not because there are four registers.

With 3 bit fields and `count` 8, every register can be named. With 3 bit fields and `count` 16, R8 to R15 exist but no instruction can name them. The file takes the field's value modulo `count`, so a 4 bit field and `count` 8 name R0 to R7 twice over.

### Small numbers in fields

An operand of type value or address can sit in a field too. The assembler puts the number in the field's bits and reports an error when it does not fit. A 4 bit field takes 0 to 15, and also -8 to -1, which it stores as the same four bits as 8 to 15. A relative operand, a distance to a label, is checked as a signed number, -8 to 7 in 4 bits.

The microcode chooses how the field is read back. `iw.field3` puts field 3 on the bus as it is, with 0 in every bit above it, so 1111 reads as 15. `iw.sfield3` copies the field's top bit into every bit above it, so 1111 reads as -1. One instruction can read its field signed and another the same field unsigned.

### Operands in the next cell

A field can not hold a full 16 bit number or address. An operand that is not in a field takes the cell after the word, as with whole cells, and the microcode reads it with `pc.output, mem.loadmar` and then `mem.output, pc.inc`. One instruction can mix both: `ADDI Rd, Rs, value` puts the two registers in fields and the value in the next cell.

### Fields that share bits

In the layout above, fields 2 and 3 both use bits 3 to 1. That is allowed, because no instruction puts operands in both: `ADD Rd, Rs, Rt` uses fields 0, 1 and 2, and `LSLI Rd, Rs, n`, a shift by a count from 0 to 15, uses 0, 1 and 3. Overlapping fields let instructions of different shapes share one word. Within one instruction, two operands can not be in fields that share a bit, and the editor reports it.

## Setting it up

In the Microcode tab, **Instruction word** switches between **Whole cells** and **Opcode fields**. With fields it shows the 16 bits of the word, the number of opcode bits and how many instructions they leave room for, and one row per field with its low bit and width, under **＋ Field**. Each instruction's operands then have a choice under **In the word**: a field, or **next cell**.

In the program editor, hover a mnemonic to see its word bit by bit. Each operand in a field shows as its letter:

```
000110 aaa bbb ccc -     ADD R0, R1, R2: opcode 6, then Rd, Rs and Rt
```

## Reading a field in microcode

A field read is an ordinary bus transfer and takes a tick like any other, so it shows on the bus in Run. Where a whole cell machine reads a register number from memory in two ticks, a field machine reads it in one:

```
whole cells:  pc.output, mem.loadmar  /  mem.output, pc.inc, rf.select
fields:       iw.field1, rf.select
```

A multiport register file goes further: it reads the register numbers straight from its fields, so no step selects a register at all, and `rf.outputa` and `rf.outputb` put two registers on two buses in the same tick.

## On a breadboard

Every part of this is a chip and some wire, the same as the rest of ExµArch.

- **The instruction word** is a 16 bit latch, two 74HC574 octal flip-flops, whose clock is enabled by `iw.load`.
- **The mapping ROM** is an EEPROM. Bus bits 15 to 10 go to six of its address pins, and its data pins go to the parallel load inputs of the step counter, such as four 74HC161s. `ir.load` is the counter's load enable. You burn one entry per opcode with the first step address of that instruction, and 0 in entry 0 and every unused entry.
- **Each field** is a 74HC244 or 74HC541 tri-state buffer. Its inputs are wired to the latch outputs for the field's bits, its outputs drive the low lines of the fields bus, and inputs tied to ground drive 0 on the bus lines above. Its output enable is the field line, `iw.field0`. A signed field is a second buffer with the field's top bit wired to all the upper inputs instead of ground.
- **The fields are fixed.** Field 1 is always bits 6 to 4, because that is where its buffer's wires go. That is why the microcode lists the fields once for the machine, and an instruction only selects among them. Two fields can share bits, because two buffers can read the same latch outputs.
- **A multiport register file's port** has its register select pins wired straight to the latch outputs of one field. That is what its `readA`, `readB` and `write` parameters describe.

## Whole cells or fields

Both kinds of machine work in ExµArch, and a machine without `opcodeBits` decodes exactly as before. Fields make programs smaller and faster, and they are how real instruction sets are encoded. They also add a step to every explanation: the word has a layout, some of its bits go to the decoder and others around it, and the step counter is no longer the instruction. [FIELD-16](exuarch:package/FIELD-16) is [RISC-16](exuarch:package/RISC-16) with fields. It runs the same programs in about 40% fewer cells and 20% fewer ticks.

## See also

- [Opcodes are addresses](exuarch:guide/opcodes-are-addresses), the tutorial that builds whole cell decoding
- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [Flags and conditions](exuarch:guide/flags-and-conditions)
- [Operands](exuarch:guide/operands)
- [instructionWord reference](exuarch:reference/instructionWord) and [multiportRegisterFile reference](exuarch:reference/multiportRegisterFile)
