; Code is data
; A von Neumann machine keeps its program and its data in the same memory, so an instruction is just a few cells that
; any other instruction can read and write. This program prints the alphabet without ever holding a letter in a
; register: OUT 'A' prints its own operand, the cell right after its opcode, and ADD [R1 + 1], 1 adds one to that
; cell, so the next time round the same OUT prints the next letter. Then it stops on a HLT it copied over its own
; first instruction.
start:  CLT                    ; overwritten with HLT at the end
        LEA   R1, letter       ; R1 points at the OUT below: its opcode, then its operand at R1 + 1
        MOV   R2, 26
letter: OUT   'A'              ; print the operand, which is only 'A' the first time
        ADD   [R1 + 1], 1      ; [R1 + 1] = [R1 + 1] + 1: rewrite that operand to the next letter
        SUB   R2, 1
        JNE   letter
        MOV   [start], [stop]  ; copy a whole instruction, as data, over the first one
        JMP   start            ; and run it
stop:   HLT                    ; never reached here: it runs at start instead
