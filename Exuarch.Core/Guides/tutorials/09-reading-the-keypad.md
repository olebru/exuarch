# Reading the keypad

Until now every program has done the same thing each time it runs. In this last tutorial the machine from [Subroutines and the stack](exuarch:guide/subroutines-and-the-stack) gets a keypad, and the program waits for keys, shows which one you pressed, and stops on space.

Skipped a tutorial, or lost your machine? Load [Tutorial 9 · Reading the keypad](<exuarch:package/Tutorial 9 · Reading the keypad>), under **Tutorial** in **Machines**: it is the machine this tutorial starts from.

## Add a keypad

1. Open [Hardware design](<exuarch:tab/Hardware design>) and drag a **keypad** onto the bus. Its ID is already `keypad`.

The [keypad](exuarch:reference/keypad) has the four arrow keys and space, and one line, `keypad.output`. It puts one bit per key on the bus: 1 for up, 2 for down, 4 for left, 8 for right and 16 for space, or 0 when nothing is pressed. A key reads as down while it is held. A quick tap is remembered until the program reads the keypad, so a slow machine does not miss it.

## Write KEYS

`KEYS` loads `a` with the keys. It has no operands and one step:

- step 1: `keypad.output` `a.load` `ir.reset`

The keypad reads like a register: it drives its bits and `a` stores them.

## Write the program

Open [Program](exuarch:tab/Program) and replace the program with:

```asm
wait:   KEYS
        LBI 0
        CMP
        JNZ got
        JMP wait

got:    LBI 16
        CMP
        JNZ show
        HLT

show:   LBI '0'
        ADD
        OUTA
        JMP wait
```

The program polls: it reads the keypad over and over.

- **wait** reads the keys and compares them with 0. While nothing is pressed Z is 1, `JNZ` does not jump and `JMP wait` reads again.
- **got** runs when a key is down. If it is space, `a` equals 16, Z is 1 and the program halts.
- **show** adds the character `'0'` to the key's bit and prints it, so up shows `1`, down `2`, left `4` and right `8`. Then it goes back to waiting.

`JMP` is the one you wrote in [Ending an instruction](exuarch:guide/ending-an-instruction); it jumps without looking at the flags.

## Run it

1. Open [Run](exuarch:tab/Run) and press **▶ Run** (Space).
2. Click **Use the keyboard** on the keypad, so the arrow keys and space go to the keypad instead of running and pausing. Or press the keys on screen with the mouse.
3. Tap the arrow keys and watch the LCD, then press space.

A tap shows once. Hold a key down, though, and it shows again every time round the loop, because the keypad reads as down for as long as you hold it. Programs that must see each press once compare the keys with the last value they read.

Polling keeps the CPU busy doing nothing but asking. The other way is to let the keypad interrupt the program when a key goes down, which [IRQ-16](exuarch:package/IRQ-16) does.

Things to try:

- Print letters instead of digits: `U` for up, `D` for down, with a `CMP` for each key.
- Wait for the key to be let go before reading the next one.

## Next

You have built a CPU with an accumulator, arithmetic, conditional jumps, subroutines and input, from a bus and a clock. From here:

- [Devices and control lines](exuarch:guide/devices-and-control-lines) and [Memory and banks](exuarch:guide/memory-and-banks) cover the other devices you can build with.
- [Interrupts](exuarch:guide/interrupts) shows how a device can break into a running program.
- [Packages](exuarch:guide/packages) covers saving your machine and sharing it.
- The examples, starting with [BYOC-16](exuarch:package/BYOC-16), are complete machines to take apart.
