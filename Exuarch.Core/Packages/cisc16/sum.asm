; Sum a table, with five addressing modes
; Each instruction names how it addresses its operands: MOV_RA is "a register, from an absolute address", ADD_RP is
; "a register, plus the cell a register points at, which then steps on". R0 sums, R1 walks the table, R2 counts.
              CLT
              MOV_RI  R0, 0       ; immediate: the number itself
              LEA     R1, table   ; the address of the table, not what is in it
              MOV_RA  R2, count   ; absolute: the cell at count
next:         ADD_RP  R0, R1      ; post-increment: R0 = R0 + [R1], then R1 = R1 + 1
              SUB_RI  R2, 1
              JNE     next
              MOV_AR  total, R0   ; an absolute destination: the sum is kept in memory
              CALL    print_number
              HLT

count:        .DATA   5
table:        .DATA   10, 20, 30, 40, 50
total:        .DATA   0

; print_number: prints R0 in decimal without leading zeros. Uses R3 to R5.
; Each place counts how often its value can be subtracted. R5 points at the place value, so [R5] is read with
; register indirect operands, CMP_NI and SUB_RN, without loading it into a register first.
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
