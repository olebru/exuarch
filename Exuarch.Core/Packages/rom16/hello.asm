; Hello from ROM
; The text is not in this program: it is in the ROM, at address 0 (see the ROM's contents in Hardware design).
; ROMPTR points the ROM's MAR at it once, and every NEXT reads a cell and moves the MAR on by itself.
        ROMPTR  0           ; the string is the first thing in the ROM
loop:   NEXT                ; A = the next character, and the ROM's MAR steps on
        JZ      done        ; the 0 at the end of the .STRING
        OUT
        JMP     loop
done:   HLT
