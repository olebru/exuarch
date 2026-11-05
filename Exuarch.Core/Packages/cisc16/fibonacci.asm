; Recursive Fibonacci, with stack frames
; fib(n) calls itself twice, so every call needs its own copy of n and of the first result. Each call builds a
; stack frame: ENTER R6, 1 saves the caller's frame pointer, points R6 at the new frame and makes room for one
; local. In the frame, [R6 + 2] is the argument pushed by the caller, [R6 + 1] the return address, [R6] the saved
; frame pointer and [R6 - 1] the local. LEAVE R6 takes the frame down again.
              CLT
              MOV    R2, 0         ; n
loop:         PUSH   R2            ; the argument
              CALL   fib           ; R0 = fib(n)
              FREE   1             ; drop the argument
              CALL   print_number
              OUT    ' '
              ADD    R2, 1
              CMP    R2, 11
              JNE    loop
              HLT

; fib: R0 = fib(n), for the n pushed before the call. Uses R1.
fib:          ENTER  R6, 1
              MOV    R1, [R6 + 2]  ; R1 = n, the argument
              CMP    R1, 2
              JCS    small         ; fib(0) = 0 and fib(1) = 1
              SUB    R1, 1
              PUSH   R1
              CALL   fib           ; fib(n - 1)
              FREE   1
              MOV    [R6 - 1], R0  ; keep it in the local: R1 and R0 are used again by the next call
              MOV    R1, [R6 + 2]
              SUB    R1, 2
              PUSH   R1
              CALL   fib           ; fib(n - 2)
              FREE   1
              ADD    R0, [R6 - 1]  ; R0 = fib(n - 2) + the local
              LEAVE  R6
              RET
small:        MOV    R0, R1
              LEAVE  R6
              RET

; print_number: prints R0 in decimal without leading zeros. Uses R3 to R5.
print_number: MOV    R4, R0        ; R4 = what is left
              LEA    R5, places
skip:         CMP    [R5], 1
              JEQ    places_on     ; the units are always printed
              CMP    [R5], R4
              JCS    places_on     ; place < number: the first one to print
              JEQ    places_on
              ADD    R5, 1         ; a leading zero, on to the next place
              JMP    skip
places_on:    MOV    R3, '0'
count_up:     CMP    R4, [R5]
              JCS    show          ; borrow: less than the place value is left
              SUB    R4, [R5]
              ADD    R3, 1
              JMP    count_up
show:         OUT    R3
              CMP    [R5], 1
              JEQ    printed       ; those were the units
              ADD    R5, 1
              JMP    places_on
printed:      RET

places:       .DATA  10000, 1000, 100, 10, 1
