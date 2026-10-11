; Interrupts: three things at once, and the main program never polls
; The main loop only counts. Everything else happens in the interrupt handler:
; irq0 the timer, every 20000 ticks: the clock on the LCD moves on
; irq1 a key press: the key counter moves on (capture the keyboard on the keypad panel and press arrows)
; irq2 the blitter finished a square: the next square of the checkerboard starts
; Memory 1000-1005: the time, keys and squares counters as two digit characters, 1010: the main loop count,
; 1020: the interrupt cause, 1030-1032: the next square's x, y and colour.
start:       CLT
             CLS
             LDI      '0'
             STA      1000
             STA      1001
             STA      1002
             STA      1003
             STA      1004
             STA      1005
             LDI      0
             STA      1030
             STA      1031
             LDI      0xFFFF
             STA      1032
             SETV     handler     ; where interrupts go
             TPERI    20000       ; a timer interrupt every 20000 ticks
             TGO
             CALL     square      ; the first square; the rest start from the blitter's interrupt
             CALL     show
             EI
main:        LDA      1010        ; the main program just counts, and never polls anything
             INA
             STA      1010
             JMP      main

; The interrupt handler. Fetch has already pushed the PC and flags and switched interrupts off.
handler:     PSA
             PSB
             CAUSE                ; which source raised it?
             STA      1020
             ACK                  ; clear what is handled here
             LDA      1020
             ANDI     1
             JEQ      no_timer
             LBA      1000        ; the timer: one more tick of the clock
             CALL     bump
no_timer:    LDA      1020
             ANDI     2
             JEQ      no_key
             LBA      1002        ; a key press
             CALL     bump
no_key:      LDA      1020
             ANDI     4
             JEQ      no_blit
             LBA      1004        ; the blitter finished a square: count it and start the next
             CALL     bump
             CALL     next_square
no_blit:     CALL     show
             POB
             POA
             RTI                  ; back to the main loop, interrupts on again

; bump: adds one to the two digit counter at B (tens) and B + 1 (ones).
bump:        INB
             LDX
             INA
             STX
             CMPI     ':'         ; past '9'
             JNE      bumped
             LDI      '0'
             STX
             DEB
             LDX
             INA
             STX
bumped:      RET

; next_square: moves on to the next square of the board and starts it, unless the board is full.
next_square: LDA      1032
             EORI     0xFFFF      ; white and black alternate
             STA      1032
             LDA      1030
             ADDI     80
             STA      1030
             CMPI     640
             JNE      square
             LDI      0           ; next row, starting on the other colour
             STA      1030
             LDA      1032
             EORI     0xFFFF
             STA      1032
             LDA      1031
             ADDI     80
             STA      1031
             CMPI     480
             JNE      square
             RET                  ; the board is full
square:      LDA      1030
             BX
             LDA      1031
             BY
             LDA      1032
             BC
             BWI      80
             BHI      80
             GO
             RET

; show: time, keys and squares on the first line, the main loop count in hex on the second.
show:        CLT
             LBA      t_time
             CALL     print
             LDA      1000
             OUT
             LDA      1001
             OUT
             LBA      t_keys
             CALL     print
             LDA      1002
             OUT
             LDA      1003
             OUT
             LBA      t_squares
             CALL     print
             LDA      1004
             OUT
             LDA      1005
             OUT
             LBA      t_main
             CALL     print
             LDA      1010
             LSRI     12
             CALL     hex
             LDA      1010
             LSRI     8
             ANDI     15
             CALL     hex
             LDA      1010
             LSRI     4
             ANDI     15
             CALL     hex
             LDA      1010
             ANDI     15
             CALL     hex
             RET

; hex: prints A (0 to 15) as a hex digit.
hex:         CMPI     10
             JCS      decimal
             ADDI     55          ; 'A' - 10
             OUT
             RET
decimal:     ADDI     '0'
             OUT
             RET

; print: prints the zero terminated string at B.
print:       LDX
             CMPI     0
             JEQ      printed
             OUT
             INB
             JMP      print
printed:     RET

t_time:      .STRING  "time "
t_keys:      .STRING  "  keys "
t_squares:   .STRING  "  squares "
t_main:      .STRING  10, "main loop "

