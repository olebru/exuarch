; Fibonacci on the LCD, Harvard style
; Variables are in data memory on dbus, the table of place values in program memory on ibus.
; Data memory: 0 a, 1 b, 2 what is left to print, 3 digit, 4 printed a digit yet, 5 place table pointer.
        CLT
        LDI    0
        STA    0           ; a = 0
        LDI    1
        STA    1           ; b = 1
next:   LDA    1
        CALL   print       ; print b; CALL keeps the return address on the stack in data memory
        LDI    ' '
        OUT
        LDA    0
        LDB    1
        ADD                ; A = a + b
        JCS    done        ; carry: the next number does not fit in 16 bits
        STA    2
        LDA    1
        STA    0           ; a = b
        LDA    2
        STA    1           ; b = a + b
        JMP    next
done:   HLT

; print: prints A in decimal without leading zeros. There is no divide: each digit counts how often its
; place value can be subtracted.
print:  STA    2
        LBA    places
        STB    5           ; pointer into the place table in program memory
        LDI    0
        STA    4           ; nothing printed yet
place:  LDB    5
        LPM                ; A = the place value, read across the bridge
        TAB                ; B = the place value, kept until the next place
        LDI    '0'
        STA    3           ; the digit
count:  LDA    2
        CMP
        JCS    show        ; borrow: less than the place value is left
        SUB
        STA    2
        LDA    3
        INA
        STA    3
        JMP    count
show:   TBA
        CMPI   1
        JEQ    digit       ; the units digit is always printed
        LDA    3
        CMPI   '0'
        JNE    digit
        LDA    4
        CMPI   0
        JEQ    skip        ; a leading zero
digit:  LDA    3
        OUT
        LDI    1
        STA    4
skip:   TBA
        CMPI   1
        JEQ    return
        LDA    5
        INA
        STA    5
        JMP    place
return: RET

places: .DATA  10000, 1000, 100, 10, 1

