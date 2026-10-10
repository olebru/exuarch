; Mandelbrot, 80 x 60
; The whole set, the same picture as DSP-16's, pixel for pixel.
; MPOINT starts a point, MITER runs z = z * z + c until it escapes, and MPUT keeps its colour in LINE and steps
; to the next point. Each row is worked out first, then drawn from LINE with PLOT blocks.
; The picture spans -2.5 to 1.25 across and -1.40625 to 1.40625 down, each point a block of 8 by 8 pixels.
        CLS
        MOVI    R0, 0
        MOVI    R1, 16385
        SLIM    R1          ; escaped once x * x + y * y passes 4
        MOVI    R1, 32
        SMAX    R1          ; at most 32 steps a point
        MOVI    R1, 192
        SDX     R1          ; the step between points
        MOVI    R6, -10240
        SCX     R6          ; the left edge, kept in R6 for every row
        MOVI    R1, -5760
        SCY     R1          ; the top
        MOVI    R1, 60
row:    LINEAT  R0          ; work the row out into LINE
        MOVI    R2, 20
point:  MPOINT
        MITER
        MPUT
        MPOINT
        MITER
        MPUT
        MPOINT
        MITER
        MPUT
        MPOINT
        MITER
        MPUT
        DBNZ    R2, point
        MOVI    R3, 8
lines:  LINEAT  R0          ; then draw it, 8 lines of pixels
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        PLOT8
        DBNZ    R3, lines
        MROW    R6
        DBNZ    R1, row
        HLT
