# Assembly

A program in ExµArch is written in assembly, but the language has no instructions of its own. The mnemonics are whatever the machine's microcode defines, so the same editor assembles BYOC-16's `LAI`, RISC-16's `ADD R0, R1, R2` and your own `OUT`. What stays the same from machine to machine is the syntax around them: labels, operands, literals, data and comments.

## A line

Each line holds at most one statement:

```asm
loop:   LAI    'A'        ; a label, a mnemonic, one operand and a comment
        OUT
        JMP    loop
```

- A **label** comes first and ends in `:`. It names the address of whatever follows it on the line, or of the next line that puts something in memory. It starts with a letter, `_` or `.` and goes on with letters, digits and `_`.
- The **mnemonic** is an instruction from the [microcode](exuarch:guide/microcode), or a directive such as `.DATA`. Mnemonics and directives match without regard to case, so `lai` is `LAI`. Labels do not: `Loop` and `loop` are two labels.
- **Operands** follow the mnemonic, separated by commas.
- A **comment** starts with `;` and runs to the end of the line.

Any spaces or tabs separate the columns. A line can be only a label, only a comment, or empty.

## Literals

A number is written in decimal (`42`) or hexadecimal (`0x2A`). A minus straight before it makes it negative, stored as the 16 bit two's complement: `-1` is 65535, and the lowest is `-32768`. A character in single quotes (`'A'`) is its Latin-1 code, 65. An older form with a leading `#` (`#42`, `#'A'`) means the same thing, and formatting drops the `#`.

Every value must fit in a 16 bit cell, 0 to 65535. A larger number is an error.

## Operands and memory

Each opcode and each operand takes one 16 bit memory cell, in the order they are written. The opcode is the address in the decoder ROM where that instruction's steps begin (see [fetch and the instruction register](exuarch:guide/fetch-and-the-instruction-register)). The instruction's microcode determines what its operands mean. The assembler only checks that you give as many as the instruction declares.

A label used as an operand is its address. If the instruction declares that operand as a value rather than an address, the assembler warns you. It is usually a mistake, such as `LAI loop` where `LDA loop` was meant. See [operands](exuarch:guide/operands).

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

The editor on the [Program](exuarch:tab/Program) tab checks the program as you type and underlines each problem where it is. The problems include an unknown mnemonic, the wrong number of operands, an unknown label, a label defined twice and a missing `,`. The editor's toolbar counts the errors and warnings, and the Program tab shows a badge while the program does not assemble. Hover over a mnemonic to see what the instruction does and which operands it takes. Completion offers the instructions and directives at the start of a line and the labels after a mnemonic.

Formatting lines up the columns: labels at the left, mnemonics and operands in their own columns, comments after them. It also writes each mnemonic the way the instruction set spells it. The editor tidies each line as you press Enter or paste, and the **Format** button (Shift+Alt+F) does the whole program.

## The listing

On the [Run](exuarch:tab/Run) tab, the Program panel lists the program as it was assembled. Each line shows its address, its cells, its label and the source. The instruction that is running is highlighted, and data lines are set apart from instructions. Click a line's gutter to set a breakpoint at its address.

## See also

- [Operands](exuarch:guide/operands)
- [Microcode](exuarch:guide/microcode)
- [Memory and banks](exuarch:guide/memory-and-banks)
- [Your first machine](exuarch:guide/first-machine)
