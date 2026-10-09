; Colour gradient on the screen
; Red grows from left to right and green from top to bottom. R1 is the colour at the left edge of the row, R2 counts
; the rows, R3 is the colour being plotted. Each PLOT moves the cursor on, and a full row wraps to the next one.
        CLS
        MOVI  R1, 0x0010      ; blue at half, red and green at 0
        MOVI  R2, 0
row:    MOV   R3, R1          ; colour at the left edge
level:  PLOT  R3              ; 20 pixels of this red level, unrolled
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        PLOT  R3
        ADDI  R3, R3, 0x0800  ; next red level; the 32nd carries out of 16 bits
        BCC   level
        ADDI  R2, R2, 1
        CMPI  R2, 480
        BEQ   done
        ANDI  R0, R2, 7       ; Z when the row count is a multiple of 8
        BNE   row
        ADDI  R1, R1, 0x0020  ; one green level more
        B     row
done:   HLT
