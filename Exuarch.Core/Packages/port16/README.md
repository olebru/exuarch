# PORT-16

[FIELD-16](exuarch:package/FIELD-16) with a different register file. FIELD-16's file has one port: a select input that takes a register number from the bus, and one output and one load line that act on that register. Every operand costs a tick to select and a tick to move. PORT-16's file has three ports, two for reading and one for writing, and their register numbers come straight from the instruction word. The programs are the same source and assemble to the same cells. They run in about 25% fewer ticks than on FIELD-16.

## The register file

[R0-R7](exuarch:device/rf) is a `multiportRegisterFile`. Its address inputs are wired to fields of [WORD](exuarch:device/iw), so the registers it reads and writes change the moment a new word is latched, with no select step:

| Port | Field | Bus | Line |
|---|---|---|---|
| read a | 1, bits 6 to 4 | main | `outputa` |
| read b | 2, bits 3 to 1 | portb | `outputb` |
| write | 0, bits 9 to 7 | main | `loadw` |

The two read ports drive different buses, so both operands of `ADD R0, R1, R2` reach the ALU registers in the same tick: R1 through main into ALU A, R2 through portb into ALU B. In the next tick the ALU puts the sum on main and `loadw` stores it in R0. FIELD-16 needs six ticks for the same instruction. `MOV R1, R2` is one tick, port a straight into the write port.

## What changed from FIELD-16

- ALU B sits on portb, the bus of read port b. WORD's fields bus is portb too, so the shift count of LSLI and LSRI goes straight into ALU B.
- [BRIDGE](exuarch:device/bridge), a `dualPortRegister`, links main and portb. Values from memory, such as the number after ADDI or CMPI, cross it into ALU B, and the value STR stores crosses it the other way.
- Every instruction that reads one register names it in field 1, and STR names its value register in field 2, so each read lands on a port. The assembler places the operands, so the source is unchanged.
- The register file's `readA`, `readB` and `write` parameters say which field feeds each port. Change them in [Hardware design](<exuarch:tab/Hardware design>) and the microcode's field lists must follow.

[Opcode fields](exuarch:guide/opcode-fields) explains the word layout this machine shares with FIELD-16, and [Bridges between buses](exuarch:guide/bridges) the bridge.

## Things to try

1. Run [Fibonacci on the LCD](<exuarch:program/Fibonacci on the LCD>) here and in FIELD-16, and compare the cycle count when it halts.
2. Tick through one ADD in [Run](exuarch:tab/Run). The register file highlights the three registers WORD names, and both ALU registers load in the same tick.
3. ADDI takes four ticks, two of them to fetch its value through the bridge. Give it a 4 bit field like LSLI, and small additions lose those two ticks.
4. Move ALU B to main and delete portb. Count how many ticks ADD needs when both read ports have to share one bus.
