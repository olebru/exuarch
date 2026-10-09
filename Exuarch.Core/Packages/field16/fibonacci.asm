; Fibonacci on the LCD
; ADD R0, R1, R2 makes a + b in a third register and leaves both where they are. print_number is called with
; BL R7 and returns with BX R7; it keeps to R0 and R3 to R5, so a and b in R1 and R2 survive the call.
              CLT
              MOVI   R1, 0       ; a
              MOVI   R2, 1       ; b
next:         MOV    R0, R2
              BL     R7, print_number
              MOVI   R0, ' '
              OUT    R0
              ADD    R0, R1, R2  ; R0 = a + b
              BCS    done        ; carry: it does not fit in 16 bits
              MOV    R1, R2
              MOV    R2, R0
              B      next
done:         HLT

; print_number: prints R0 in decimal without leading zeros. Uses R0 and R3 to R5.
; There is no divide: each place counts how often its value can be subtracted.
print_number: MOV    R4, R0      ; R4 = what is left of the number
              ADR    R5, places  ; R5 walks the table of place values
skip:         LDR    R0, R5      ; R0 = the place value
              CMPI   R0, 1
              BEQ    places_on   ; the units are always printed
              CMP    R0, R4
              BCS    places_on   ; place < number: the first one to print
              BEQ    places_on   ; place = number: also the first
              ADDI   R5, R5, 1   ; a leading zero, try the next place
              B      skip
places_on:    LDR    R0, R5      ; R0 = the place value
              MOVI   R3, '0'     ; R3 counts up the character to print
count:        CMP    R4, R0
              BCS    show        ; borrow: less than the place value is left
              SUB    R4, R4, R0
              ADDI   R3, R3, 1
              B      count
show:         OUT    R3
              CMPI   R0, 1
              BEQ    return      ; those were the units
              ADDI   R5, R5, 1
              B      places_on
return:       BX     R7

places:       .DATA  10000, 1000, 100, 10, 1
