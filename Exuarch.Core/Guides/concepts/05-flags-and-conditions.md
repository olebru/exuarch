# Flags and conditions

A machine that can only run the same steps every time can not branch. Flags are how it does: the ALU records facts about its last result in a status register, and the decoder uses those bits to select which steps of an instruction run. A conditional jump is nothing more than an instruction whose steps depend on a flag.

## The status register

The decoder names one register as its status register (`decoder.status`), usually a `statusRegister`. Its four low bits are the flags:

| Bit | Value | Flag |
|-----|-------|------|
| 0 | 1 | Z, zero |
| 1 | 2 | C, carry |
| 2 | 4 | V, overflow |
| 3 | 8 | N, negative |

It is otherwise an ordinary register with `output`, `load`, `reset`, `inc` and `dec`, so microcode can save the flags and load them back, which is what [IRQ-16](exuarch:package/IRQ-16) does around an interrupt.

## What the ALU sets

An `alu` is connected to its status register as `status`, and every operation replaces all four flags at the end of the tick, so a flag an operation does not set is cleared. Every operation sets Z when the result is 0 and N from the result's top bit, which is its sign when the value is read as a signed number. C and V depend on the operation:

- `add`: C when the sum carries out of 16 bits, V on signed overflow.
- `sub` and `cmp`: C when a is less than b as unsigned numbers (a borrow), V on signed overflow. `cmp` sets the flags without putting the result on the bus.
- `and`, `orr`, `eor`: C and V are 0.
- `lsl`, `lsr`: a shifted by the low 4 bits of b. C is the last bit shifted out, V is 0.

For example `0x7FFF + 1` gives `0x8000` with N and V set: the result looks negative, and V indicates that is because the signed sum overflowed.

## Comparing numbers

A 16 bit value can be read as unsigned (0 to 65535) or signed (-32768 to 32767), and `cmp` sets flags for both readings at once:

| After `cmp` | Means |
|---|---|
| Z=1 | a equals b |
| C=1 | a is below b, unsigned |
| N≠V | a is less than b, signed |

N alone is not enough for a signed comparison, because the subtraction can overflow: comparing `0x8000` (-32768) with 1 gives `0x7FFF`, a positive result with V set. N and V differ, so a is less than b, which is right. Comparing `0xFFFF` with 1 gives N=1 and V=0: as signed numbers -1 is less than 1, while C=0 indicates that as unsigned numbers 65535 is not below 1.

A step condition tests one value per flag, so a "less than" branch on N≠V takes two steps, one for N=1 V=0 and one for N=0 V=1.

## The interrupt condition

There is a fifth condition, I. It does not come from the status register but from the machine's interrupt controller (`decoder.interrupts`), and it is 1 when an enabled interrupt is pending. The decoder samples it when the instruction register is 0, at the start of fetch, and holds it for the whole instruction, so a request can not switch routines halfway through one. See [Interrupts](exuarch:guide/interrupts).

## Step conditions

Any step can have a condition: a required value, 0 or 1, for any of N, V, C, Z and I. In the [Microcode](exuarch:tab/Microcode) tab each step has five flag buttons; click one to cycle it through any, 1 and 0. In the machine's JSON a condition is a `when`:

```json
{ "when": { "Z": true }, "signals": ["pc.output", "mem.loadmar"] }
```

A step runs only when every flag it names matches. A step with no condition always runs.

## Variants

For each combination of flags the decoder selects the steps whose conditions match, in order, and that list is what runs. Here is `JEQ` from [BYOC-16](exuarch:package/BYOC-16), jump if equal:

```
step 1:            pc.inc
step 2: when Z=1   pc.output, mem.loadmar
step 3: when Z=1   pc.load, mem.output, regi.reset
step 4: when Z=0   regi.reset, pc.inc
```

With Z=1 it runs steps 1, 2 and 3: read the operand and load it into the program counter. With Z=0 it runs steps 1 and 4: skip the operand. The second tick of the instruction is step 2 in one case and step 4 in the other.

Each variant must return to fetch on its own, and the microcode editor checks every variant separately. **Preview flags** above the steps shows what runs for a chosen set of flags, numbering the steps by tick and dimming the ones that do not run.

The four flags are read afresh every tick, so an instruction that changes the flags partway through switches variant from the next tick on. The I condition is the exception: it is held for the whole instruction.

## The decoder ROM

The decoder is a lookup table. Its address is the flags and the micro step together:

```
ROM address = (status & 0x1F) << 16 | micro step
```

The low 16 bits are the instruction register, and above them are the five condition bits, I at bit 4 (value 0x10) and N, V, C, Z below it. For every one of the 32 combinations each instruction's block holds that variant's steps, so looking up the next tick's signals is a single read.

The **Decoder ROM** tab in [Run](exuarch:tab/Run) shows this: the address split into its I N V C Z bits and the 16 micro step bits, the ROM address they make, and a table of the rows for the current flags with a column per control line. Tick through a conditional jump with Z set and with it clear to watch the address change.

## See also

- [Microcode](exuarch:guide/microcode)
- [Loops and flags](exuarch:guide/loops-and-flags), a tutorial
- [The status register](exuarch:reference/statusRegister)
- [The ALU](exuarch:reference/alu)
- [RISC-16](exuarch:package/RISC-16), with branches on Z, C and N
