; Colour cycling
; The 80 x 60 picture is worked out once, and then its colours turn, about 96,000 ticks a frame.
; MSLOT keeps where each point sits in the palette, 1 to 32 or 64 for the set, in DATA. Every frame adds a phase
; to each slot and looks the colour up in the CYCLE ROM, a row at a time into LINE, then draws the row as blocks
; of 4 by 4 pixels in the middle of the screen. Nothing is worked out again: the colours flow outwards through
; the bands, the way Fractint cycled its palette.
        CLS
        MOVI    R0, 0
        MOVI    R1, 16385
        SLIM    R1
        MOVI    R1, 32
        SMAX    R1
        MOVI    R1, 192
        SDX     R1
        MOVI    R6, -10240
        SCX     R6
        MOVI    R1, -5760
        SCY     R1
        DATAAT  R0
        MOVI    R1, 60
row:    MOVI    R2, 20
point:  MPOINT
        MITER
        MSLOT
        MPOINT
        MITER
        MSLOT
        MPOINT
        MITER
        MSLOT
        MPOINT
        MITER
        MSLOT
        DBNZ    R2, point
        MROW    R6
        DBNZ    R1, row
        MOVI    R7, 0       ; the phase
frame:  SPH     R7
        DATAAT  R0
        MOVI    R4, 120     ; the top of the picture on the screen
        MOVI    R5, 160     ; and its left edge
        MOVI    R1, 60
arow:   LINEAT  R0
        PHY
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        CYC
        MOVI    R3, 4
aline:  PX      R5
        PY      R4
        LINEAT  R0
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        PLOT4
        ADDI    R4, R4, 1
        DBNZ    R3, aline
        DBNZ    R1, arow
        ADDI    R7, R7, 1   ; turn the colours one step
        CMPI    R7, 32
        BNE     frame
        MOVI    R7, 0
        B       frame
