; Mandelbrot, 640 x 480
; The whole set, the same picture as DSP-16's, pixel for pixel.
; Every pixel is its own point, so MPLOT puts each colour straight on the screen, whose cursor moves right and
; wraps at the end of a row. Eight points to a loop, so DBNZ costs a little less per point.
        CLS
        MOVI    R1, 16385
        SLIM    R1          ; escaped once x * x + y * y passes 4
        MOVI    R1, 32
        SMAX    R1          ; at most 32 steps a point
        MOVI    R1, 24
        SDX     R1          ; the step between points
        MOVI    R6, -10240
        SCX     R6          ; the left edge, kept in R6 for every row
        MOVI    R1, -5760
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
