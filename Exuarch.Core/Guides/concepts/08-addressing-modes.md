# Addressing modes

An **addressing mode** is the way an instruction finds its operand: the number itself, a register, or a cell in memory found one of several ways. This page is for machines that offer several of them, like [CISC-16](exuarch:package/CISC-16), and for building your own. It assumes you know [assembly](exuarch:guide/assembly) and [operands](exuarch:guide/operands).

## Writing a mode

In ExµArch every mode is microcode, so a machine has an instruction of its own for each mode, as CISC-16 does with `ADD_RR`, `ADD_RX` and so on. With a **syntax** on each of those instructions, a program writes the mode the way real assemblers do, with square brackets for memory:

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

## How a line matches an instruction

The assembler resolves every instruction line to one instruction of the microcode before it writes any cells:

1. **A plain line is not changed.** If the mnemonic is the name of an instruction and no operand has brackets, the line assembles to that instruction, operand by operand. `ADD_RX R0, R6, -1` on CISC-16 and `MOV R1, R2` on RISC-16 never take the next step.
2. **Otherwise the written forms are compared.** Every instruction whose syntax starts with the written mnemonic is a candidate, so `MOV` on CISC-16 has 34 of them (see *Where the candidates come from* below). A candidate matches when it has the same number of operands, brackets in the same places, the same post-increments and the same number of terms in each bracket, and when each term fits the type of its operand:

   | Written | register operand | value | address or relative |
   |---|---|---|---|
   | `R1` | fits | no | no |
   | `5`, `'A'`, `-1` | no | fits best | fits |
   | `table` | no | fits, with a warning | fits best |

   A `-` only goes before a number, and never before a register.
3. **The best fit is selected.** When several candidates match, the one whose terms fit best is selected: `MOV R0, 5` is immediate and `MOV R0, [table]` absolute, while `MOV R0, [R1]` is register indirect because `R1` only fits a register. When no candidate matches, the error lists the forms the mnemonic does have. Two candidates that fit equally well are an error too, and the microcode editor reports such a pair before any program uses it.

The line then assembles as if it had been written with the instruction's own name and its operands in the instruction's order. `MOV [R2 + 3], R0` is `MOV_XR R2, 3, R0`, the same cells. Hover the mnemonic to see which instruction a line matched. The instruction names keep working, and a program can mix both.

## Where the candidates come from

When the assembler is created for a machine, it reads the syntax of every instruction in the microcode once and files each instruction under the first word of its syntax. An instruction with no syntax is not filed. For CISC-16 the file starts like this:

| Word | Syntax | Instruction |
|---|---|---|
| `MOV` | `MOV {0}, {1}` | `MOV_RI` (register, value) |
| `MOV` | `MOV {0}, {1}` | `MOV_RR` (register, register) |
| `MOV` | `MOV {0}, [{1}]` | `MOV_RA` (register, address) |
| `MOV` | `MOV {0}, [{1}]` | `MOV_RN` (register, register) |
| `MOV` | `MOV {0}, [{1} + {2}]` | `MOV_RX` (register, register, value) |
| `ADD` | `ADD {0}, [{1}]` | `ADD_RN` (register, register) |

The candidates for a line are the instructions filed under the line's first word, compared without regard to case. The instruction names are not part of this search. They only matter twice: a plain line that uses a name directly goes to that instruction (rule 1 above), and the instruction selected in step 3 is the one whose opcode is written.

So the names are free to be anything. CISC-16's `MOV_` prefix is a naming habit, not a rule. An instruction named `FETCH_OFFSET` with the syntax `MOV {0}, [{1} + {2}]` is a `MOV` candidate, and `MOV R0, [R1 + 4]` can select it. Give `MOV_RN` the syntax `LD {0}, [{1}]` instead, and it is no longer a `MOV` candidate: `MOV R0, [R1]` then matches nothing, and `LD R0, [R1]` selects it.

## Giving your own instructions a syntax

Each instruction can have a **syntax** in the [Microcode](exuarch:tab/Microcode) tab, next to its operand types, or as `syntax` in the machine file. It is written like a line of the program, with `{0}`, `{1}` and so on where the instruction's operands go:

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

## What each form costs

Every instruction takes a block of rows in the decoder ROM, one row per step, and its opcode is the first row of that block (see [fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)). A mode is not decoded separately: each instruction has its own copy of the steps that work out its addresses. A full set of modes therefore grows as destination modes times source modes, and every combination is a full block. On CISC-16 the 34 `MOV` instructions take 309 of the 1,734 rows in use, and `ADD`, `SUB`, `AND` and `CMP` take most of the rest. The decoder has room for 65,536 rows, so the ROM is rarely the limit, but each combination is another block of steps to write and check. Add the combinations your programs use.

## Relative operands and PC-relative addressing

A **relative** operand holds a distance instead of an address. `BRA loop`, with a relative operand, assembles to `loop` minus the address of the cell after the whole instruction, so the same cells branch to the same place wherever the program is loaded. Backwards is negative: a branch to itself in a two cell instruction is -2, stored as 65534. A number written for a relative operand is the distance itself, so `BRA 0` continues with the next instruction. In an opcode field a relative operand is checked as a signed number: a 4 bit field holds -8 to 7.

A relative operand pairs with microcode that adds the program counter. By the time the microcode has read the last operand cell of an instruction, the program counter points at the next instruction, which is exactly where the assembler measures the distance from. CISC-16's `BRA label` reads the distance into one input of its address unit and the program counter into the other, then loads their sum back into the program counter:

```
step 1: pc.output, mem.loadmar
step 2: mem.output, pc.inc, ay.load
step 3: pc.output, ax.load
step 4: agu.add, pc.load, ir.reset
```

`MOV R0, [PC + table]` does the same, but sends the sum to the memory's address register and reads the cell there. Code built from relative branches and PC-relative reads runs wherever it is loaded, which is why real CPUs use them for most jumps.

## Mistakes

On top of the usual [assembly](exuarch:guide/assembly) errors, the editor reports:

- a `]` missing, a term missing between `+` and `]`, or two terms without a `+` or `-` between them;
- brackets on an instruction that has no syntax with brackets;
- a written line that matches none of the forms of its mnemonic, with the forms listed;
- a relative distance that does not fit its opcode field.

Completion lists the written mnemonics, such as `MOV`, with their forms, and registers or labels inside brackets. Formatting puts one space on each side of a `+` or `-` inside brackets, and the listing on the [Run](exuarch:tab/Run) tab shows operands as they were written.

## See also

- [Assembly](exuarch:guide/assembly)
- [Operands](exuarch:guide/operands)
- [Opcode fields](exuarch:guide/opcode-fields)
- [CISC-16](exuarch:package/CISC-16), every addressing mode in one machine
