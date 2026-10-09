; Backwards through a buffer
; PUT stores A and moves the buffer's MAR forward; BACK moves it back and reads. The program never counts or
; keeps an index: the address register walks by itself, forwards with incmar and backwards with decmar.
        BUFPTR  0
        LDI     0
        PUT                 ; a 0 first, so reading backwards knows where to stop
        ROMPTR  0
copy:   NEXT                ; a character from the ROM
        JZ      print
        PUT                 ; into the buffer, and on to the next cell
        JMP     copy
print:  BACK                ; back one cell and read it: the last character first
        JZ      done        ; back at the 0
        OUT
        JMP     print
done:   HLT
