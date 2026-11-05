          CLT
          MOV    R0, [PC + length]
          LEA    R1, word
          LEA    R2, reversed
          MOV    R3, 0
copy:     MOV    R4, [R1 + R3]
          SUB    R0, 1
          MOV    [R2 + R0], R4
          ADD    R3, 1
          CMP    R0, 0
          JNE    copy
          MOV    [PC + copied], R3
          MOV    R3, 0
show:     MOV    R4, [R2 + R3]
          OUT    R4
          ADD    R3, 1
          CMP    R3, [copied]
          JEQ    done
          BRA    show
done:     HLT

length:   .DATA  7
word:     .DATA  "EXUARCH"
reversed: .DATA  0, 0, 0, 0, 0, 0, 0
copied:   .DATA  0
