# SHARP-16

A machine built to be the target of a compiler. It runs a small subset of C#: `static` methods with parameters, locals and recursion, `int`, `bool`, `char` and `string`, arrays, `if`, loops and `switch`, and `Console.Write` to the LCD. The compiler and a live editor that shows the assembly as you type come with it.

## The idea

A compiler wants three things from a machine: somewhere to work out an expression, somewhere to keep a function's variables, and a cheap way to call and return. SHARP-16 has one of each.

- **An accumulator and a second operand.** Every expression ends up in [A](exuarch:device/acc). A binary operation works on B and A and leaves the result in A: `ADD` is A = B + A. The compiler puts the left side in B with [TAB](exuarch:instruction/TAB) when the right side is simple, and on the stack with [PUSH](exuarch:instruction/PUSH) and [POP](exuarch:instruction/POP) when it is not.
- **A frame pointer and an address adder.** A function's parameters and locals sit at fixed distances from [FP](exuarch:device/fp), so the compiler knows each one's offset. [LDL](exuarch:instruction/LDL) and [STL](exuarch:instruction/STL) take that offset and the [ADDRESS ADDER](exuarch:device/agu), a second ALU wired to FP and OFFSET, works out the address without touching A, B or the flags.
- **Calls that build their own frames.** [CALL](exuarch:instruction/CALL) pushes the return address, [ENTER](exuarch:instruction/ENTER) n saves FP and makes room for n locals, and [RET](exuarch:instruction/RET) k puts everything back and drops the k - 2 arguments the caller pushed.

## A frame

The stack grows down from the top of memory and SP points at the top item. Every register starts at 0, so the first push wraps SP round to 65535.

| Address | Holds |
|---|---|
| FP + 2 ... | the arguments, the last one nearest |
| FP + 1 | the return address |
| FP | the caller's FP |
| FP - 1 ... | the locals |

For `static int Add(int a, int b)`, `a` is at FP + 3 and `b` at FP + 2, and the method ends with `RET 4`.

## Arithmetic

[MUL](exuarch:instruction/MUL), [DIV](exuarch:instruction/DIV) and [MOD](exuarch:instruction/MOD) use the [MULTIPLIER](exuarch:device/mac), with its shift at 0 so it works on whole numbers. Division rounds toward 0 and the remainder takes the sign of the dividend, as in C#. A product that does not fit in 16 bits saturates at -32768 or 32767 instead of wrapping, and dividing by 0 gives 32767 or -32768 rather than an exception.

The jumps after [CMP](exuarch:instruction/CMP) are signed: [JLT](exuarch:instruction/JLT) jumps when N and V differ, [JGT](exuarch:instruction/JGT) when Z is clear and N and V agree.

## Things to try

1. Open a program, switch on ⚡ Max and run it.
2. Step through `CALL`, `ENTER` and `RET` and watch SP and FP build and take down a frame.
3. Write C# in the SHARP-16 editor, download the package with your program in it and import it here.
