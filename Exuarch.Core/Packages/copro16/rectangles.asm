; Rectangles: the CPU writes while the blitter paints
; For each rectangle in the table the CPU writes its position, size and colour to the blitter's registers, starts it with GO, and at once
; prints the rectangle's name on the LCD while the blitter fills it on the video bus. Before the next job it
; waits, printing a dot now and then so you can see the CPU is still running.
; Memory 1000: table pointer, 1001: wait counter, 1002: the name being printed.
           CLT
           CLS
           LBA      rects       ; B walks the table: x, y, width, height, colour, name
next:      LDX                  ; x, or 0xFFFF at the end of the table
           CMPI     0xFFFF
           JEQ      finish
wait:      BST                  ; the blitter must be idle before a new job is written to it
           CMPI     0
           JEQ      ready
           LDA      1001        ; count the waits and show a dot every 1024
           INA
           STA      1001
           ANDI     1023
           JNE      wait
           LDI      '.'
           OUT
           JMP      wait
ready:     LDX
           BX                   ; left column
           INB
           LDX
           BY                   ; top row
           INB
           LDX
           BW                   ; width
           INB
           LDX
           BH                   ; height
           INB
           LDX
           BC                   ; colour
           INB
           GO                   ; the blitter starts; everything below runs while it paints
           LDX                  ; the name
           INB
           STA      1002
           TBA
           STA      1000        ; keep the table pointer
           LDA      1002
           TAB                  ; B = the name's first character
           LDI      ' '
           OUT
print:     LDX
           CMPI     0
           JEQ      printed
           OUT
           INB
           JMP      print
printed:   LDA      1000
           TAB
           JMP      next

finish:    BST                  ; wait for the last rectangle
           CMPI     0
           JNE      finish
           LBA      done_text
last:      LDX
           CMPI     0
           JEQ      stop
           OUT
           INB
           JMP      last
stop:      HLT

; x, y, width, height, colour, name
rects:     .DATA    40, 40, 560, 400, 0x2945, frame
           .DATA    80, 80, 200, 150, 0xF800, red
           .DATA    360, 80, 200, 150, 0x07E0, green
           .DATA    80, 260, 200, 140, 0x001F, blue
           .DATA    360, 260, 200, 140, 0xFFE0, yellow
           .DATA    250, 190, 140, 100, 0xFFFF, white
           .DATA    0xFFFF
frame:     .STRING  "frame"
red:       .STRING  "red"
green:     .STRING  "green"
blue:      .STRING  "blue"
yellow:    .STRING  "yellow"
white:     .STRING  "white"
done_text: .STRING  " done"


