; Count down from 9
; The counter is kept on the stack, not in a register. DUP makes a copy for each thing that uses it up: one to print,
; one for JZ to test.
        PUSH  9
loop:   DUP               ; n n
        PUSH  '0'
        ADD
        OUT               ; n
        PUSH  1
        SUB               ; n-1
        DUP               ; n-1 n-1
        JZ    done        ; n-1
        JMP   loop
done:   POP
        HLT
