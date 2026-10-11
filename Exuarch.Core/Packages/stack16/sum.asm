; The sum of 1 to 100, printed in decimal
; Two stacks at work: the data stack holds the numbers, the return stack holds where each CALL came from. print
; calls itself for the digits in front, so every digit stays on the data stack while its call is on the return stack.
        PUSH  0           ; sum
        PUSH  100         ; sum i
add:    DUP               ; sum i i
        JZ    show        ; sum i
        SWAP              ; i sum
        OVER              ; i sum i
        ADD               ; i sum+i
        SWAP              ; sum+i i
        PUSH  1
        SUB               ; sum+i i-1
        JMP   add
show:   POP               ; sum
        CALL  print
        HLT

; print: pops a number and prints it in decimal.
; There is no divide: n is split into n / 10 and n mod 10 by subtracting 10 until that would go below 0.
print:  PUSH  0           ; n q
        SWAP              ; q n
divide: DUP               ; q n n
        PUSH  10
        SUB               ; q n n-10
        DUP               ; q n m m
        JN    split       ; q n m
        SWAP              ; q m n
        POP               ; q m
        SWAP              ; m q
        PUSH  1
        ADD               ; m q+1
        SWAP              ; q+1 m
        JMP   divide
split:  POP               ; q r: n / 10 and n mod 10
        SWAP              ; r q
        DUP               ; r q q
        JZ    units       ; r q
        CALL  print       ; r: the digits in front are printed
        JMP   digit
units:  POP               ; r
digit:  PUSH  '0'
        ADD
        OUT
        RET
