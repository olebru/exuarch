# FIELD-16

[RISC-16](exuarch:package/RISC-16), with its operands packed into the instruction word. The programs are the same source, and the machine is the same apart from one new device and the way the decoder reads a word. They assemble to about 40% fewer cells and run in about 20% fewer ticks.

## The word

The top 6 bits of every instruction word are the opcode. The 10 bits below hold up to three register numbers, in fields that are always in the same place:

| Field | Bits | Used for |
|---|---|---|
| 0 | 9 to 7 | Rd, or the only register |
| 1 | 6 to 4 | Rs |
| 2 | 3 to 1 | Rt |
| 3 | 3 to 0 | the shift count of LSLI and LSRI, 0 to 15 |

`ADD R0, R1, R2` is one word, `000110 000 001 010 0`, where RISC-16 needs four cells. Opcode 0 is fetch, so ADD, the sixth instruction, is opcode 6. Values and addresses that need all 16 bits, like `MOVI R0, 1000` or `B loop`, still follow in a cell of their own. Hover any mnemonic in [Program](exuarch:tab/Program) to see its word drawn bit by bit.

## What changed from RISC-16

- [WORD](exuarch:device/iw), an `instructionWord`, keeps the fetched word. Its `field0` to `field3` lines put one field on the bus.
- Fetch loads the word into the instruction register and into WORD in the same tick. The instruction register loads through a mapping ROM, which turns the opcode into the address of the instruction's first step.
- Every operand read from memory, two ticks each, became one field read. ADD went from 10 ticks to 6, plus fetch.
- The microcode says `"opcodeBits": 6` and lists the four fields, and each instruction says which field each operand goes in.

Look at the same instruction in both machines in [Microcode](exuarch:tab/Microcode) to see the difference step by step. [Opcode fields](exuarch:guide/opcode-fields) explains the decoder, and how you would wire it with a latch, an EEPROM and a few buffers.

## Things to try

1. Run [Fibonacci on the LCD](<exuarch:program/Fibonacci on the LCD>) here and in RISC-16, and compare the cycle count when it halts.
2. Tick through one ADD in [Run](exuarch:tab/Run) and watch WORD put a register number on the bus.
3. Write `LSLI R0, R1, 16` in a program. The assembler refuses it: 16 does not fit in the 4 bit field.
4. ADDI still takes its value from the next cell. Give it a 4 bit field of its own, using field 3 like LSLI does. Small additions then fit in one word, and the microcode loses two ticks.
