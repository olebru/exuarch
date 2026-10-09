# ROM-16

Two small ideas that real computers lean on all the time.

- **A ROM**, read-only memory that already holds something when the machine starts: text, tables and constants that the program only reads.
- **An address register that moves by itself.** Every memory's MAR has `incmar` and `decmar` lines. Enabled in the same tick as a read or a write, they step the address on once the cell has been read or written. Walking through memory then costs no extra instruction and no index register.

## The ROM

[ROM](exuarch:device/rom) holds 256 cells, and nothing can write them. What it holds is part of the machine, not of the program. Select the ROM in [Hardware design](<exuarch:tab/Hardware design>) and press **Edit contents…** to see them. They are written like data in a program: `.STRING` for text, `.DATA` for numbers, with labels and comments. The editor shows each cell as it fills, and **Insert a table** writes a sine, cosine, ramp or squares table for you.

This one holds `"Hello from ROM!"` at address 0, then 64 heights for a sine wave from address 16, then a 0 that marks the end of the table.

## The instructions

| Instruction | What it does |
|---|---|
| [ROMPTR](exuarch:instruction/ROMPTR) `n` | point the ROM's MAR at address `n` |
| [NEXT](exuarch:instruction/NEXT) | A = the ROM cell at its MAR, and the MAR moves on; Z when the cell was 0 |
| [BUFPTR](exuarch:instruction/BUFPTR) `n` | point the buffer's MAR at address `n` |
| [PUT](exuarch:instruction/PUT) | store A in the buffer, and its MAR moves on |
| [BACK](exuarch:instruction/BACK) | the buffer's MAR moves back, then A = that cell; Z when it was 0 |
| [LDI](exuarch:instruction/LDI) `n` | A = `n` |
| [OUT](exuarch:instruction/OUT) | print A on the LCD |
| [TAX](exuarch:instruction/TAX), [TXA](exuarch:instruction/TXA), [INX](exuarch:instruction/INX), [CPX](exuarch:instruction/CPX) `n` | copy A and X, add 1 to X, compare X with `n` |
| [PX](exuarch:instruction/PX), [PY](exuarch:instruction/PY), [PLOT](exuarch:instruction/PLOT) `colour` | screen column from X, row from A, plot a colour there |
| [JMP](exuarch:instruction/JMP), [JZ](exuarch:instruction/JZ), [JNZ](exuarch:instruction/JNZ) `label` | jump, always or on the Z flag |
| [HLT](exuarch:instruction/HLT) | stop |

NEXT is the whole trick in one tick: `rom.output`, `a.load` and `rom.incmar` together. The cell goes into A and, at the end of the same tick, the ROM's MAR moves to the next cell. Two more ticks clear B and compare, so JZ can stop at a 0. PUT is the same thing in the other direction, `a.output`, `buf.load` and `buf.incmar`, and BACK steps first with `buf.decmar` and reads after.

## Things to try

1. [Hello from ROM](<exuarch:program/Hello from ROM>): tick through it in [Run](exuarch:tab/Run) and watch `rom.mar` count up by itself, one per NEXT, while the program never touches it.
2. [Backwards through a buffer](<exuarch:program/Backwards through a buffer>) copies the string into [BUFFER](exuarch:device/buf) with PUT, then reads it back with BACK. The buffer's MAR counts up and then down again, and the text comes out reversed.
3. [A sine wave from a table](<exuarch:program/A sine wave from a table>) draws ten waves from 64 numbers. The machine has no multiplier and no sine; the ROM remembers the answers.
4. Change the ROM's text in **Edit contents…**, and run Hello from ROM again.
5. Insert a cosine table with a label of its own after the sine table, and point the wave program at it. The cells list in the editor shows the address its label lands on.
