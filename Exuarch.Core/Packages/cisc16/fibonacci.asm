; Recursive Fibonacci, with stack frames
; fib(n) calls itself twice, so every call needs its own copy of n and of the first result. Each call builds a
; stack frame: ENTER R6, 1 saves the caller's frame pointer, points R6 at the new frame and makes room for one
; local. In the frame, [R6 + 2] is the argument pushed by the caller, [R6 + 1] the return address, [R6] the saved
; frame pointer and [R6 - 1] the local. LEAVE R6 takes the frame down again.
              CLT
              MOV_RI  R2, 0       ; n
loop:         PUSH_R  R2          ; the argument
              CALL    fib         ; R0 = fib(n)
              FREE    1           ; drop the argument
              CALL    print_number
              OUT_I   ' '
              ADD_RI  R2, 1
              CMP_RI  R2, 11
              JNE     loop
              HLT

; fib: R0 = fib(n), for the n pushed before the call. Uses R1.
fib:          ENTER   R6, 1
              MOV_RX  R1, R6, 2   ; R1 = n, the argument
              CMP_RI  R1, 2
              JCS     small       ; fib(0) = 0 and fib(1) = 1
              SUB_RI  R1, 1
              PUSH_R  R1
              CALL    fib         ; fib(n - 1)
              FREE    1
              MOV_XR  R6, -1, R0  ; keep it in the local: R1 and R0 are used again by the next call
              MOV_RX  R1, R6, 2
              SUB_RI  R1, 2
              PUSH_R  R1
              CALL    fib         ; fib(n - 2)
              FREE    1
              ADD_RX  R0, R6, -1  ; R0 = fib(n - 2) + the local
              LEAVE   R6
              RET
small:        MOV_RR  R0, R1
              LEAVE   R6
              RET

; print_number: prints R0 in decimal without leading zeros. Uses R3 to R5.
print_number: MOV_RR  R4, R0      ; R4 = what is left
              LEA     R5, places
skip:         CMP_NI  R5, 1
              JEQ     places_on   ; the units are always printed
              CMP_NR  R5, R4
              JCS     places_on   ; place < number: the first one to print
              JEQ     places_on
              ADD_RI  R5, 1       ; a leading zero, on to the next place
              JMP     skip
places_on:    MOV_RI  R3, '0'
count_up:     CMP_RN  R4, R5
              JCS     show        ; borrow: less than the place value is left
              SUB_RN  R4, R5
              ADD_RI  R3, 1
              JMP     count_up
show:         OUT_R   R3
              CMP_NI  R5, 1
              JEQ     printed     ; those were the units
              ADD_RI  R5, 1
              JMP     places_on
printed:      RET

places:       .DATA   10000, 1000, 100, 10, 1
