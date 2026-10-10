; Lightning at the top of the set
; The tip at the top of the set, around -0.1011 + 0.9563i, 24 times nearer, at a step of 1, the smallest 4.12 has,
; and with 128 steps a point. The same picture as DSP-16's, pixel for pixel.
        CLS
        MOVI    R1, 16385
        SLIM    R1          ; escaped once x * x + y * y passes 4
        MOVI    R1, 128
        SMAX    R1          ; at most 128 steps a point
        MOVI    R1, 1
        SDX     R1          ; the step between points
        MOVI    R6, -734
        SCX     R6          ; the left edge, kept in R6 for every row
        MOVI    R1, 3677
        SCY     R1          ; the top
        MOVI    R1, 480
row:    MOVI    R2, 80
point:  MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        MPOINT
        MITER
        MPLOT               ; straight to the screen
        DBNZ    R2, point
        MROW    R6
        DBNZ    R1, row
        HLT
