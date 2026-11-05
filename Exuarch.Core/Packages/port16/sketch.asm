; Sketch with the arrow keys
; The arrow keys move the pen and space changes its colour. In the Run view, click the keypad to use the keyboard.
; R1 x, R2 y, R3 colour, R0 the keys. ANDI R4, R0, 1 tests a key into R4 and leaves R0 as it is, so the keys are read
; once and tested five times. The program runs until you stop it.
           CLS
           MOVI  R1, 320
           MOVI  R2, 240
           MOVI  R3, 0xFFE0      ; yellow
draw:      PX    R1
           PY    R2
           PLOT  R3
           IN    R0              ; R0 = the keys: 1 up, 2 down, 4 left, 8 right, 16 space
           ANDI  R4, R0, 1
           BEQ   not_up
           CMPI  R2, 0
           BEQ   not_up          ; already at the top
           SUBI  R2, R2, 1
not_up:    ANDI  R4, R0, 2
           BEQ   not_down
           CMPI  R2, 479
           BEQ   not_down        ; already at the bottom
           ADDI  R2, R2, 1
not_down:  ANDI  R4, R0, 4
           BEQ   not_left
           CMPI  R1, 0
           BEQ   not_left
           SUBI  R1, R1, 1
not_left:  ANDI  R4, R0, 8
           BEQ   not_right
           CMPI  R1, 639
           BEQ   not_right
           ADDI  R1, R1, 1
not_right: ANDI  R4, R0, 16
           BEQ   pause
           ADDI  R3, R3, 0x18E3  ; a little more red, green and blue
pause:     MOVI  R4, 60          ; wait a moment so the pen does not race at full speed
wait:      SUBI  R4, R4, 1
           BNE   wait
           B     draw
