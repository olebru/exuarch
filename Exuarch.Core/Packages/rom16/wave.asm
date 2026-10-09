; A sine wave from a table
; The heights are worked out once and kept in the ROM from address 16; this program never multiplies or
; calls sin. Each column reads the next height with NEXT, and the 0 after the table starts it again.
        LDI     0
        TAX                 ; X = column 0
again:  ROMPTR  16          ; wave starts at ROM address 16, after "Hello from ROM!" and its 0
column: NEXT                ; A = the next height
        JZ      again       ; the 0 after the table: back to its start
        PY
        PX
        PLOT    0x07E0      ; green
        INX
        CPX     640
        JNZ     column
        HLT
