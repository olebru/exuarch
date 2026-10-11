; Checkerboard: the CPU works out the next square while the blitter fills this one
; An 8 x 6 board of 80 x 80 squares. The CPU starts a square, then works out the next square's position and
; colour while the blitter paints, and only then waits. That overlap is what a coprocessor is for.
; Memory 0: x, 1: y, 2: colour.
        CLT
        CLS
        LDI   0
        STA   0
        STA   1
        LDI   0xFFFF      ; white first
        STA   2
square: BST               ; wait until the blitter is free
        CMPI  0
        JNE   square
        LDA   0
        BX
        LDA   1
        BY
        LDA   2
        BC
        BWI   80
        BHI   80
        GO                ; paint this square ...
        LDI   '#'         ; ... and meanwhile work out the next one
        OUT
        LDA   2
        EORI  0xFFFF      ; white and black alternate
        STA   2
        LDA   0
        ADDI  80
        STA   0
        CMPI  640
        JNE   square
        LDI   0           ; next row, starting on the other colour
        STA   0
        LDA   2
        EORI  0xFFFF
        STA   2
        LDA   1
        ADDI  80
        STA   1
        CMPI  480
        JNE   square
done:   BST               ; wait for the last square
        CMPI  0
        JNE   done
        HLT

