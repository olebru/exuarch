# Opcode fields

In most ExµArch machines an opcode is the address of its instruction's first step, it takes a whole cell, and every operand takes a cell of its own after it. That is the simplest decoder there is, and the one [Opcodes are addresses](exuarch:guide/opcodes-are-addresses) builds. Real CPUs pack more into one word: a few bits say which instruction it is, and the bits below them name registers or hold small numbers. This page is about that second way, which a machine chooses with one setting in its microcode.

## The word

With `"opcodeBits": 6` in the microcode, the top 6 bits of an instruction word are its **opcode**. Opcode 0 is fetch, as in every ExµArch machine, so a cell of zeros goes straight back to fetch. The first instruction after fetch is 1, the next 2, and so on, which leaves room for 63 instructions. The 10 bits below them belong to **fields**, which the microcode lists once for the whole machine:

```json
"opcodeBits": 6,
"fields": [
  { "low": 7, "bits": 3 },
  { "low": 4, "bits": 3 },
  { "low": 1, "bits": 3 },
  { "low": 0, "bits": 4 }
]
```

Field 0 is bits 9 to 7, field 1 bits 6 to 4, field 2 bits 3 to 1, and field 3 bits 3 to 0. Fields 2 and 3 share bits, and that is allowed: no instruction uses both. Each instruction says which field each of its operands goes in, `"fields": [0, 1, 2]` for `ADD Rd, Rs, Rt`. Operands it does not put in a field, or marks with -1, still take a cell after the word, which is how a full 16 bit number or address is given. In the Microcode tab, **Instruction word** sets the opcode bits and the fields, and each operand of an instruction has a choice of field or next cell. The assembler packs the fields and checks that every value fits. Hover a mnemonic in the program editor to see its word drawn bit by bit:

```
000110 aaa bbb ccc -     ADD R0, R1, R2: opcode 6, then Rd, Rs and Rt
```

## Fetch

Fetch loads the word into two devices at once:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, ir.load, iw.load, pc.inc
```

The instruction register, the decoder's micro step counter, does not take the word as it is. Its `load` goes through a **mapping ROM**: the opcode bits pick an entry, and the entry is the address of that instruction's first step. Entry 0, and every opcode no instruction has, maps to step 0, so the counter goes straight back to fetch. The [instructionWord](exuarch:reference/instructionWord) device keeps the whole word for the rest of the instruction. It has two ports: `data`, where it loads the word, and `fields`, where its field lines drive. On a machine with one bus both are the same bus. [BLAZE-16](exuarch:package/BLAZE-16) keeps its program on a bus of its own, so WORD loads from the program bus and its fields drive the data path's bus, where the registers are.

## Reading a field

`iw.field0` puts field 0 on the bus, in the low bits, with 0 in every bit above it. `iw.sfield0` does the same with the field's top bit copied into every bit above it, so a negative field stays negative: a 4 bit field holding 1111 reads as 15 through `field3` and as -1 through `sfield3`. A field read is an ordinary bus transfer and takes a tick like any other, so it shows on the bus in Run. Where a whole cell machine reads a register number from memory in two ticks, a field machine reads it in one:

```
whole cells:  pc.output, mem.loadmar  /  mem.output, pc.inc, rf.select
fields:       iw.field1, rf.select
```

## On a breadboard

Every part of this is a chip and some wire, the same as the rest of ExµArch.

- **The instruction word** is a 16 bit latch, two 74HC574 octal flip-flops, whose clock is enabled by `iw.load`.
- **The mapping ROM** is an EEPROM. Bus bits 15 to 10 go to six of its address pins, and its data pins go to the parallel load inputs of the step counter, such as four 74HC161s. `ir.load` is the counter's load enable. You burn one entry per opcode with the first step address of that instruction, and 0 in entry 0 and every unused entry.
- **Each field** is a 74HC244 or 74HC541 tri-state buffer. Its inputs are wired to the latch outputs for the field's bits, its outputs drive the low lines of the fields bus, and inputs tied to ground drive 0 on the bus lines above. Its output enable is the field line, `iw.field0`. A signed field is a second buffer with the field's top bit wired to all the upper inputs instead of ground.
- **The fields are fixed.** Field 1 is always bits 6 to 4, because that is where its buffer's wires go. That is why the microcode lists the fields once for the machine, and an instruction only chooses among them. Two fields can share bits, because two buffers can read the same latch outputs.

## Whole cells or fields

Both kinds of machine work in ExµArch, and a machine without `opcodeBits` decodes exactly as before. Fields make programs smaller and faster, and they are how real instruction sets are encoded. They also add a step to every explanation: the word has a layout, some of its bits go to the decoder and others around it, and the step counter is no longer the instruction. [FIELD-16](exuarch:package/FIELD-16) is [RISC-16](exuarch:package/RISC-16) with fields. It runs the same programs in about 40% fewer cells and 20% fewer ticks.

## See also

- [Fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)
- [Operands](exuarch:guide/operands)
- [instructionWord reference](exuarch:reference/instructionWord)
