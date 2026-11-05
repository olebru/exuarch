# Assembly

A program in ExµArch is written in assembly, but the language has no instructions of its own. The mnemonics are whatever the machine's microcode defines, so the same editor assembles BYOC-16's `LAI`, RISC-16's `ADD R0, R1, R2` and your own `OUT`. What stays the same from machine to machine is everything around them: labels, literals, operands, the brackets of an addressing mode, data and comments. This page covers all of it, from a single line to how a written operand like `[R1 + 4]` becomes cells in memory.

## A line

Each line holds at most one statement:

```asm
loop:   LAI    'A'        ; a label, a mnemonic, one operand and a comment
        OUT
        JMP    loop
```

- A **label** comes first and ends in `:`. It names the address of whatever follows it on the line, or of the next line that puts something in memory. It starts with a letter, `_` or `.` and goes on with letters, digits and `_`.
- The **mnemonic** is an instruction from the [microcode](exuarch:guide/microcode), a written form of one (see *Brackets and addressing modes* below), or a directive such as `.DATA`. Mnemonics and directives match without regard to case, so `lai` is `LAI`. Labels do not: `Loop` and `loop` are two labels.
- **Operands** follow the mnemonic, separated by commas.
- A **comment** starts with `;` and runs to the end of the line.

Any spaces or tabs separate the columns. A line can be only a label, only a comment, or empty.

## Literals

A number is written in decimal (`42`) or hexadecimal (`0x2A`). A minus straight before it makes it negative, stored as the 16 bit two's complement: `-1` is 65535, and the lowest is `-32768`. A character in single quotes (`'A'`) is its Latin-1 code, 65. An older form with a leading `#` (`#42`, `#'A'`) means the same thing, and formatting drops the `#`.

Every value must fit in a 16 bit cell, 0 to 65535. A larger number is an error.

## Labels

A label used as an operand stands for its address. The assembler reads the program twice: the first pass adds up how many cells each line takes and records where every label lands, and the second writes the cells. A label can therefore be used before the line that defines it, as `JMP done` usually is. Defining the same label twice is an error, and so is using one that is never defined.

## How a line becomes cells

Each instruction assembles to its opcode, then one 16 bit cell per operand, in the order the instruction declares them. The opcode is the address in the decoder ROM where that instruction's steps begin (see [fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)). `LAI 'A'` is two cells, the opcode of `LAI` and 65.

On a machine whose microcode sets `opcodeBits`, the opcode only fills the top bits of the word, and operands the instruction places in a **field** go into the bits below it instead of a cell of their own. `ADD R0, R1, R2` on [FIELD-16](exuarch:package/FIELD-16) is one word. The assembler packs each field and reports an error when a value does not fit. [Opcode fields](exuarch:guide/opcode-fields) explains the layout.

What an operand cell means at run time depends on the instruction's microcode. What the assembler checks depends on what the instruction declares: how many operands it takes, and what type each one is.

## Operand types

An instruction can declare a type for each operand, in the [Microcode](exuarch:tab/Microcode) tab or as `operandTypes` in the machine file. The type changes what the assembler accepts and what the editor shows on hover:

| Type | Written | Assembles to | Checked |
|---|---|---|---|
| **value** | `42`, `'A'`, `-1` | the number | a label here assembles to its address, with a warning |
| **address** | `loop`, `0x100` | the address | an unknown label is an error |
| **register** | `R0` to `R7` | the register number | only `R` and a number below the register file's count |
| **relative** | `loop`, `8` | a label's distance from the next instruction; a number as it is | the distance must fit, and in a field it is signed |

A **relative** operand is what position independent code is made of. `BRA loop`, with a relative operand, assembles to `loop` minus the address of the cell after the whole instruction, so the same cells branch to the same place wherever the program is loaded. Backwards is negative: a branch to itself in a two cell instruction is -2, stored as 65534. A number written for a relative operand is the distance itself, so `BRA 0` continues with the next instruction. In an opcode field a relative operand is checked as a signed number: a 4 bit field holds -8 to 7.

See [operands](exuarch:guide/operands) for how microcode reads each kind, and *PC-relative addressing* below for the microcode of a relative operand.

## Brackets and addressing modes

An **addressing mode** is the way an instruction finds its operand: the number itself, a register, or a cell in memory found one of several ways. A machine can give each mode an instruction of its own, as [CISC-16](exuarch:package/CISC-16) does with `ADD_RR`, `ADD_RX` and so on. With a **syntax** on each of those instructions, a program writes the mode the way real assemblers do, with square brackets for memory:

| Mode | Written | Means |
|---|---|---|
| immediate | `MOV R0, 5` | the number 5 |
| register direct | `MOV R0, R1` | the register R1 |
| absolute | `MOV R0, [total]` | the cell at the address `total` |
| register indirect | `MOV R0, [R1]` | the cell R1 points at |
| base plus displacement | `MOV R0, [R1 + 4]`, `MOV R0, [R6 - 1]` | the cell at R1 + 4, or at R6 - 1 |
| indexed, register plus register | `MOV R0, [R2 + R3]` | the cell at R2 + R3 |
| post-increment | `MOV R0, [R1]+` | the cell R1 points at, then R1 steps on |
| PC-relative | `MOV R0, [PC + table]` | the cell at a distance from the next instruction |

Inside brackets, terms are joined by `+` or `-`. A `-` before a number negates it, so `[R6 - 1]` assembles exactly like `[R6 + -1]` would: the displacement cell holds 65535. A `+` after the closing bracket marks post-increment. Fixed words such as `PC` are part of the syntax, not operands: they match their own name, in any case, and take no cell.

Brackets are only for instructions. `.DATA [1]` is an error. Outside brackets a `+` is an error too: the assembler has no arithmetic on operands, and `label + 1` does not assemble.

### How a line matches an instruction

The assembler resolves every instruction line to one instruction of the microcode before it writes any cells:

1. **A plain line keeps its instruction.** If the mnemonic is the name of an instruction and no operand has brackets, the line assembles to that instruction, operand by operand, exactly as it always has. `ADD_RX R0, R6, -1` and `MOV R1, R2` on RISC-16 never take the next step.
2. **Otherwise the written forms are compared.** Every instruction whose syntax starts with the written mnemonic is a candidate, so `MOV` on CISC-16 has 34 of them. A candidate matches when it has the same number of operands, brackets in the same places, the same post-increments and the same number of terms in each bracket, and when each term fits the type of its operand:

   | Written | register operand | value | address or relative |
   |---|---|---|---|
   | `R1` | fits | no | no |
   | `5`, `'A'`, `-1` | no | fits best | fits |
   | `table` | no | fits, with a warning | fits best |

   A `-` only goes before a number, and never before a register.
3. **The best fit is selected.** When several candidates match, the one whose terms fit best is selected: `MOV R0, 5` is immediate and `MOV R0, [table]` absolute, while `MOV R0, [R1]` is register indirect because `R1` only fits a register. When no candidate matches, the error lists the forms the mnemonic does have. Two candidates that fit equally well are an error too, and the microcode editor reports such a pair before any program reaches it.

The line then assembles as if it had been written with the instruction's own name and its operands in the instruction's order. `MOV [R2 + 3], R0` is `MOV_XR R2, 3, R0`, the same cells. Hover the mnemonic to see which instruction a line matched, and the old names keep working: a program can mix both.

### Giving your own instructions a syntax

Each instruction can have a **syntax** in the Microcode tab, next to its operand types, or as `syntax` in the machine file. It is written like a line of the program, with `{0}`, `{1}` and so on where the instruction's operands go:

```json
{ "mnemonic": "MOV_RX", "operandTypes": ["register", "register", "value"],
  "syntax": "MOV {0}, [{1} + {2}]", "steps": [ ... ] }
```

- The mnemonic in the syntax can be anything, and many instructions can share it. It does not have to be the instruction's name.
- Every operand appears exactly once. The order is free, so an instruction that reads its operands as base, displacement and source can still be written `ST {2}, [{0} + {1}]`.
- Inside brackets, write `+` between terms. A program can then write `+` or `-` before a number.
- Any other word, like `PC` or `SP`, is a fixed word that a program writes as it is.
- The operand types do the rest. Two instructions can share `MOV {0}, [{1} + {2}]` when one takes a value as `{2}` and the other a register: `[R1 + 4]` and `[R1 + R2]` match different instructions.

The microcode editor reports a syntax that does not parse, leaves out an operand or uses one twice, and two instructions with the same syntax and the same types. A syntax with no brackets whose mnemonic is the name of another instruction never matches, because rule 1 above sends that line to the instruction with the name. The editor reports that as a warning.

A syntax does not change the instruction or its microcode. The cells are the same whichever way a line is written.

### PC-relative addressing

A **relative** operand pairs with microcode that adds the program counter. By the time the microcode has read the last operand cell of an instruction, the program counter points at the next instruction, which is exactly where the assembler measures the distance from. CISC-16's `BRA label` reads the distance into one input of its address unit and the program counter into the other, then loads their sum back into the program counter:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, pc.inc, ay.load
step 3: pc.output, ax.load
step 4: agu.add, pc.load, ir.reset
```

`MOV R0, [PC + table]` does the same, but sends the sum to the memory's address register and reads the cell there. Code built from relative branches and PC-relative reads runs wherever it is loaded, which is why real CPUs use them for most jumps.

## Data: .DATA and .STRING

Two directives put values into memory without an opcode in front of them:

```asm
colours:  .DATA    0xF800, 0x07E0, 0x001F   ; one cell each
table:    .DATA    10, 'x', colours         ; numbers, characters and label addresses
name:     .DATA    "BYOC"                   ; one cell per character
greeting: .STRING  "Hello", 10              ; like .DATA, then a 0 cell
```

`.DATA` stores each operand in its own cell. A string in double quotes takes one cell per character. `.STRING` is the same, with one more cell holding 0 at the end of the line, so a program can walk the text until it reads a 0. Strings are only allowed in these two directives.

Inside strings and character literals you can write the escapes `\n` (10), `\t` (9), `\0` (0), `\\`, `\"` and `\'`. Characters must be Latin-1, up to code 255.

`.BYTE` and `.WORD` are older names for `.DATA`. They still assemble to the same cells, with a warning, because every value took one 16 bit cell whichever name you used. Any other directive is an error.

## Where the program goes

The assembled cells are loaded into the machine's program memory, starting at address 0. That memory is the device named by `programMemory` in the machine, and it has to be a `ram` device (see [memory and banks](exuarch:guide/memory-and-banks)). A program larger than that memory is an error. A machine without `programMemory` can only run an empty program.

Your program starts at address 0 only because the fetch routine reads the cell the program counter points at, and the program counter starts at 0. That is how the example machines are built, not a rule of the assembler.

## Mistakes and help

The editor on the [Program](exuarch:tab/Program) tab checks the program as you type and underlines each problem where it is. The editor's toolbar counts the errors and warnings, and the Program tab shows a badge while the program does not assemble. The problems it reports include:

- an unknown mnemonic, the wrong number of operands, an unknown label, a label defined twice and a missing `,`;
- a value that does not fit in 16 bits, or in its opcode field;
- a `]` missing, a term missing between `+` and `]`, or two terms without a `+` or `-` between them;
- brackets on an instruction that has no syntax with brackets;
- a written line that matches none of the forms of its mnemonic, with the forms listed.

Hover over a mnemonic to see what the instruction does, which operands it takes and, for a written form, which instruction the line matched. Hover over an operand to see its type and what it stands for. Completion offers the instructions, the written mnemonics and the directives at the start of a line, and registers or labels where an operand goes, inside brackets too.

Formatting lines up the columns: labels at the left, mnemonics and operands in their own columns, comments after them. It writes each mnemonic the way the instruction set spells it, and puts one space on each side of a `+` or `-` inside brackets. The editor tidies each line as you press Enter or paste, and the **Format** button (Shift+Alt+F) does the whole program.

## The listing

On the [Run](exuarch:tab/Run) tab, the Program panel lists the program as it was assembled. Each line shows its address, its cells, its label and the source, with operands as they were written. The instruction that is running is highlighted, and data lines are set apart from instructions. Click a line's gutter to set a breakpoint at its address.

## Quick reference

| Write | For |
|---|---|
| `name:` | a label at the start of a line |
| `; text` | a comment |
| `42`, `0x2A`, `-1`, `'A'` | numbers and characters |
| `R0` | a register, where the instruction takes one |
| `label` | an address, or a distance for a relative operand |
| `[x]`, `[x + y]`, `[x - 4]` | memory, on instructions with a syntax |
| `[R1]+` | post-increment, on instructions with a syntax |
| `[PC + label]` | PC-relative, on instructions with a syntax |
| `.DATA 1, 'x', "text", label` | cells without an opcode |
| `.STRING "text"` | text followed by a 0 cell |

## See also

- [Operands](exuarch:guide/operands)
- [Microcode](exuarch:guide/microcode)
- [Opcode fields](exuarch:guide/opcode-fields)
- [Memory and banks](exuarch:guide/memory-and-banks)
- [Your first machine](exuarch:guide/first-machine)
- [CISC-16](exuarch:package/CISC-16), every addressing mode in one machine
