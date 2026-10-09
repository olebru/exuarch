; Hello, world on the LCD
; print is called with BL R7, print: the return address goes into R7, and BX R7 jumps back to it. A call touches no memory.
        CLT
        ADR      R1, msg     ; R1 points at the string
        BL       R7, print
        HLT

; print: writes the zero terminated string at R1 to the LCD. Uses R0.
print:  LDR      R0, R1      ; R0 = the character R1 points at
        CMPI     R0, 0
        BEQ      done        ; a 0 ends the string
        OUT      R0
        ADDI     R1, R1, 1   ; on to the next character
        B        print
done:   BX       R7

msg:    .STRING  "Hello from FIELD-16!"
