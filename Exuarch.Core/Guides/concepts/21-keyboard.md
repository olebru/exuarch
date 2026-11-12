# The keyboard

A [`keypad`](exuarch:reference/keypad) has five keys and one bit for each. That is enough for a game, not for typing. A [`keyboard`](exuarch:reference/keyboard) has every key, and it works the way the keyboard of the first IBM PC did: it does not send characters. It sends a number for the key, a **scan code**, when the key goes down and again when it comes up, and the program decides what that key means.

## Scan codes

The keyboard sends the scan codes of the 83-key IBM PC/XT keyboard, one byte for each event:

- When a key goes down, its scan code: `A` is `0x1E`, Enter `0x1C`, the left Shift `0x2A`, space `0x39`.
- While a key is held, the same code again each time it repeats.
- When it comes up, the same code plus `0x80`: `A` coming up is `0x9E`.

The codes follow the rows of keys, not the alphabet. The top row of letters, Q W E R T Y, is `0x10` to `0x15`; the row below it, A S D F, starts at `0x1E`. The digits 1 to 0 are `0x02` to `0x0B`, and F1 to F10 are `0x3B` to `0x44`. The XT keyboard had its arrow keys on the number pad, so the arrows send the codes of 8, 4, 6 and 2 there: up `0x48`, left `0x4B`, right `0x4D` and down `0x50`. The key between the left Shift and Z on European keyboards, `<` on a Norwegian one, sends `0x56`, its code on the later 102-key keyboard. Both Shift keys, Ctrl and Alt send their own codes like any other key; there is one Ctrl and one Alt, as on the XT.

A scan code names where the key is, not what is printed on it. The key to the right of L sends `0x27` on every keyboard: it is `;` on a US one and `ø` on a Norwegian one.

## The queue

Codes wait in a queue of 32, oldest first. The keyboard has one control line:

- `output` puts the oldest code on the bus and takes it out of the queue. When the queue is empty it puts 0, which no key sends.

Every code added to the queue raises an interrupt request. When the queue is full, new codes are dropped, and the Run view shows how many.

Because the [interrupt controller](exuarch:guide/interrupts) keeps one pending bit for each source, several codes can arrive behind one request. A handler reads until it gets 0:

```
key:    IN   R0          ; the oldest scan code
        CMPI R0, 0
        BEQ  done        ; 0: the queue is empty
        ...              ; use the code
        B    key
done:
```

The mnemonics are RISC-16's; any machine whose microcode turns on the keyboard's `output` line can do the same.

## From scan code to character

A program turns a scan code into a character by looking it up in a table. In a [`rom`](exuarch:reference/rom) the table needs no instructions to set it up, and changing the layout is changing the ROM. **Edit contents…** on a ROM, in the hardware design, can insert two layouts, under *Insert a keyboard layout*:

- **US keyboard**
- **Norwegian keyboard**

Each is 256 cells. Cell *n* holds the character scan code *n* types; cell 128 + *n* holds the character it types with Shift held. A key that types nothing, such as Ctrl or an arrow, holds 0. Enter is 13, Backspace 8, Tab 9 and Escape 27. The Norwegian layout types `æ`, `ø` and `å` as their Latin-1 codes, which a [`display`](exuarch:reference/display) shows. It has no AltGr characters, such as `@` and `$`: Alt is one key on the XT, and the tables only have room for Shift.

A program that types into a display then needs one more thing than the loop above, the state of Shift:

- `0x2A` or `0x36`, a Shift going down: remember 128.
- `0xAA` or `0xB6`, a Shift coming up: remember 0.
- Any other code of `0x80` or more: a key coming up, nothing to do.
- Anything else: read the ROM at the table's start + Shift + code. 0 means the key types nothing; anything else is a character.

Adding 128 for Shift picks the second half of the table, so the lookup is the same with and without it.

## Typing on it

In the Run view, the keyboard's panel shows the codes waiting in its queue. Click **Use the keyboard**, and every key you press goes to the machine, including the ones the Run view would use itself, such as space and R. Escape goes to the machine too; tap it three times quickly to take the keyboard back. Keys that are down when the keyboard is taken back, or when the Run view loses focus, are sent as coming up, so a program never sees a Shift that stays down.

## See also

- [Interrupts](exuarch:guide/interrupts): the interrupt controller, and a handler that reads until the queue is empty
- [Memory and banks](exuarch:guide/memory-and-banks): ROMs, and reading a cell at a computed address
- [Reading the keypad](exuarch:guide/reading-the-keypad): the five key keypad, read as bits
- [keyboard reference](exuarch:reference/keyboard) and [rom reference](exuarch:reference/rom)
