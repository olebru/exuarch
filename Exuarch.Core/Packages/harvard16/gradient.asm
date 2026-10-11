; Colour gradient on the screen, Harvard style
; Red grows from left to right and green from top to bottom. Each PLOT takes two ticks: A crosses to iobus
; while the next PLOT is fetched on ibus.
; Data memory: 0 row colour, 1 rows drawn, 2 pixel colour.
        CLS
        LDI   0x0010      ; blue at half, red and green at 0
        STA   0
        LDI   0
        STA   1
row:    LDA   0
        STA   2           ; colour at the left edge
level:  LDA   2
        PLOT              ; 20 pixels of this red level, unrolled
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        PLOT
        LBI   0x0800
        ADD               ; next red level; the 32nd carries out
        STA   2           ; STA does not change the flags
        JCC   level
        LDA   1
        INA
        STA   1
        CMPI  480
        JEQ   done
        ANDI  7           ; Z when the row count is a multiple of 8
        JNE   row
        LDA   0
        ADDI  0x0020      ; one green level
        STA   0
        JMP   row
done:   HLT

