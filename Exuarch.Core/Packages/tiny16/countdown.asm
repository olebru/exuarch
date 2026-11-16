; Count down from 9 to 0
; A loop: a label marks where it starts, JUMP goes back to it, and JZ exits the loop when A reaches '0'.
        LOAD  '9'
loop:   OUT               ; print the digit in A
        SUB   '0'         ; A = the digit's value: 9, 8, ... 0
        JZ    done        ; it was '0': stop
        ADD   47          ; back to a character, one lower: '0' - 1 is 47
        JUMP  loop
done:   HALT
