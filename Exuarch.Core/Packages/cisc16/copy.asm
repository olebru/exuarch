; Copy a string, memory to memory
; MOV_PP R2, R1 copies one character from [R1] to [R2] and steps both pointers on: a whole loop body in one
; instruction, the kind of thing CISC machines were built for. CMP_XI R2, -1, 0 then reads one cell back, at the
; character just copied, through an indexed operand with a negative offset.
         CLT
         LEA      R1, message
         LEA      R2, copy
again:   MOV_PP   R2, R1      ; [R2]+ = [R1]+
         CMP_XI   R2, -1, 0   ; was that the 0 at the end?
         JNE      again
         LEA      R1, copy    ; print the copy, not the original
show:    CMP_NI   R1, 0
         JEQ      done
         OUT_P    R1          ; print [R1], then R1 = R1 + 1
         JMP      show
done:    HLT

message: .STRING  "Copied by MOV_PP"
copy:    .DATA    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
