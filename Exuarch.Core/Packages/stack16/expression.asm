; (9 - 4) + (1 + 2), worked out on the stack
; Every value is pushed, and every operation pops its two operands and pushes the result, so the program reads like
; the expression in reverse Polish notation: 9 4 - 1 2 + +. The result, 8, is turned into a character and printed.
        PUSH  9
        PUSH  4
        SUB               ; 9 - 4 = 5
        PUSH  1
        PUSH  2
        ADD               ; 1 + 2 = 3, on top of the 5
        ADD               ; 5 + 3 = 8
        PUSH  '0'
        ADD               ; the character code of the digit
        OUT
        HLT
