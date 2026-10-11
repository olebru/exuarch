; Lightning at the top of the set
; Close in on the tip at the top of the set, around -0.1011 + 0.9563 i, where it branches like lightning into
; filaments full of tiny copies of itself. The step is 1, 1/4096, the smallest step 4.12 can represent, so the picture is
; 640/4096 = 0.15625 across: 24 times closer than the whole set at 640 x 480.
; Every point c = cx + i cy runs z = z * z + c from z = c, up to 128 steps, until z is more than 2 from 0. A point
; that never gets that far is in the Mandelbrot set and is black; the others are coloured by how many steps were
; left, a palette of 32 colours that repeats. Numbers are 4.12 fixed point, 4096 is 1.0, and MUL multiplies two of
; them in one instruction.
; The picture spans about -0.1792 to about -0.0229 across and about 0.8977 to about 1.0149 down, 640 points by 480, each drawn as one pixel.
; One row of points is worked out into line first, then drawn: the screen's cursor moves right with every PLOT and
; wraps at the end of a row of pixels, so the whole screen fills in order without PX or PY.
         CLS
         ADR    R7, top
         LDR    R0, R7
         ADR    R7, cy
         STR    R0, R7      ; cy = the top
         MOVI   R0, 0
         ADR    R7, row
         STR    R0, R7
nextrow: ADR    R7, left
         LDR    R0, R7
         ADR    R7, cx
         STR    R0, R7      ; cx = the left edge
         MOVI   R0, 0
         ADR    R7, col
         STR    R0, R7
nextcol: ADR    R7, cx
         LDR    R2, R7
         ADR    R7, cy
         LDR    R3, R7
         MOV    R0, R2      ; z = c: R0 is x and R1 is y
         MOV    R1, R3
         MOVI   R6, 128     ; steps left
iter:    MUL    R4, R0, R0  ; x * x
         MUL    R5, R1, R1  ; y * y
         ADD    R7, R4, R5
         CMPI   R7, 16385
         BCC    escaped     ; x * x + y * y > 4, compared unsigned, as the sum can pass 32767
         MUL    R7, R0, R1
         ADD    R1, R7, R7
         ADD    R1, R1, R3  ; y = 2 x y + cy
         SUB    R0, R4, R5
         ADD    R0, R0, R2  ; x = x * x - y * y + cx
         SUBI   R6, R6, 1
         BNE    iter
         MOVI   R7, 0       ; still close after 128 steps: in the set, black
         B      store
escaped: SUBI   R7, R6, 1
         ANDI   R7, R7, 31  ; the palette repeats every 32 steps
         ADR    R5, palette
         ADD    R7, R7, R5
         LDR    R7, R7      ; the colour for how many steps were left
store:   ADR    R4, col
         LDR    R0, R4
         ADR    R5, line
         ADD    R5, R5, R0
         STR    R7, R5      ; line[col] = the colour
         ADDI   R0, R0, 1
         STR    R0, R4
         ADR    R4, cx
         LDR    R5, R4
         ADDI   R5, R5, 1   ; cx = cx + the step, about 0.0002
         STR    R5, R4
         CMPI   R0, 640
         BNE    nextcol
         MOVI   R5, 1       ; draw the row: 1 line of pixels
lines:   ADR    R4, line
         MOVI   R3, 640
points:  LDR    R7, R4
         PLOT   R7
         ADDI   R4, R4, 1
         SUBI   R3, R3, 1
         BNE    points
         SUBI   R5, R5, 1
         BNE    lines
         ADR    R4, cy
         LDR    R5, R4
         ADDI   R5, R5, 1   ; cy = cy + the step
         STR    R5, R4
         ADR    R7, row
         LDR    R0, R7
         ADDI   R0, R0, 1
         STR    R0, R7
         CMPI   R0, 480
         BNE    nextrow
         HLT

left:    .DATA  -734
top:     .DATA  3677
row:     .DATA  0
col:     .DATA  0
cx:      .DATA  0
cy:      .DATA  0
palette: .DATA  0x58A0, 0x6920, 0x81C0, 0x9A60, 0xAB00, 0xC3A0, 0xDC40, 0xECC0, 0xFD61, 0xFDC5, 0xFE09, 0xF66D, 0xF6D1, 0xF735, 0xF779, 0xEFDD, 0xE7BF, 0xC71E, 0xAE7D, 0x95FD, 0x755C, 0x5CBB, 0x441A, 0x2399, 0x1B18, 0x1AB6, 0x1254, 0x11D3, 0x0971, 0x090F, 0x00AE, 0x002C
line:
