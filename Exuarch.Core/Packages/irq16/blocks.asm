; Falling blocks: the classic game of seven pieces, on interrupts
; Run it at 250 kHz or faster, up to max, and capture the keyboard on the keypad panel. Then left and right
; move, up turns, down drops faster, space drops.
; A full row clears; four at once score the most. Every ten rows the level goes up and the pieces fall faster.
; Interrupts do the timing. The real time clock (irq3) beats every 16 milliseconds of real time, however fast the
; machine ticks, and counts down three counters: when the piece next falls, when a held arrow repeats, and when
; a held down arrow drops it again. So the game keeps its pace at any clock speed fast enough to draw it. The
; blitter (irq2) says when it has finished a square, so the next can start. The main loop reads the keys and
; moves the piece.
; The well is 10 squares by 20, each 20 pixels, at 220, 40 on the screen. Memory 3500-3699 holds it row by
; row, 0 for an empty square or the colour of the piece that landed there, and 3700-3899 what the screen shows
; there, so that after rows go only the squares that changed are drawn again. The stack sits above them.

start:      CLT
            CLS
            SETV     handler
            MASKI    12          ; the blitter and the real time clock interrupt; key presses are read with IN
            RTCI     16          ; a beat every 16 ms of real time: 62.5 a second
            EI
            CALL     frame
title:      LBA      t_title
            CALL     print
wait_start: LDA      seed        ; count while waiting: when the player presses space decides the pieces
            INA
            STA      seed
            IN
            ANDI     16
            JEQ      wait_start
            LDA      seed
            CMPI     0
            JNE      new_game
            INA
            STA      seed

; A new game: an empty well, no score, level 0.
new_game:   LBA      3500        ; the well and what the screen shows of it
            LDI      400
            STA      count
ng_clear:   LDI      0
            STX
            INB
            LDA      count
            SUBI     1
            STA      count
            JNE      ng_clear
            LBA      score
            LDI      7           ; the score's six digits and the hidden one before them
            STA      count
            CALL     zeros
            LBA      lines
            LDI      4
            STA      count
            CALL     zeros
            LDI      '0'
            STA      level
            LDA      delays
            STA      fall_delay
            LDI      0
            STA      over
            LDI      20          ; no blocks: the top of the stack is below the well
            STA      top
            LDI      0xFFFF      ; keys held as the game starts count once they are let go and pressed again
            STA      prev
            LDI      220         ; the well, black
            STA      r_x
            LDI      40
            STA      r_y
            LDI      200
            STA      r_w
            LDI      400
            STA      r_h
            LDI      0
            STA      r_c
            CALL     draw_rect
            CALL     random7
            STA      next
            CALL     spawn
            CALL     show

; The game loop: read the keys, move the piece, and let it fall when the beat says so.
game_loop:  LDA      over
            CMPI     0
            JNE      game_over
            IN
            STA      keys
            LDA      prev        ; new presses: keys down now that were not down last time
            EORI     0xFFFF
            TAB
            LDA      keys
            AND
            STA      newk
            LDA      keys
            STA      prev
            LDA      newk        ; left: at once, then again every few beats while held
            ANDI     4
            JEQ      held_left
            LDI      10
            STA      das_cnt
            CALL     move_left
            JMP      right
held_left:  LDA      keys
            ANDI     4
            JEQ      right
            LDA      das_cnt
            CMPI     0
            JNE      right
            LDI      3
            STA      das_cnt
            CALL     move_left
right:      LDA      newk
            ANDI     8
            JEQ      held_right
            LDI      10
            STA      das_cnt
            CALL     move_right
            JMP      turn
held_right: LDA      keys
            ANDI     8
            JEQ      turn
            LDA      das_cnt
            CMPI     0
            JNE      turn
            LDI      3
            STA      das_cnt
            CALL     move_right
turn:       LDA      newk
            ANDI     1
            JEQ      drop
            CALL     rotate
drop:       LDA      newk
            ANDI     16
            JEQ      soft
            CALL     hard_drop
            JMP      game_loop
soft:       LDA      keys
            ANDI     2
            JEQ      fall
            LDA      soft_cnt
            CMPI     0
            JNE      fall
            LDI      2
            STA      soft_cnt
            LDA      fall_delay
            STA      fall_cnt
            CALL     step_down
            JMP      game_loop
fall:       LDA      fall_cnt
            CMPI     0
            JNE      game_loop
            LDA      fall_delay
            STA      fall_cnt
            CALL     step_down
            JMP      game_loop

game_over:  CALL     show
            IN                   ; a space still held from the last drop does not count
            STA      prev
again:      IN                   ; wait for a new press of space
            STA      keys
            LDA      prev
            EORI     0xFFFF
            TAB
            LDA      keys
            AND
            STA      newk
            LDA      keys
            STA      prev
            LDA      newk
            ANDI     16
            JEQ      again
            JMP      new_game

; The interrupt handler: the blitter is free again, or the real time clock beat and the counters count down.
handler:    PSA
            PSB
            CAUSE
            STA      cause
            ACK
            LDA      cause
            ANDI     4
            JEQ      beat
            LDI      0
            STA      blit_busy
beat:       LDA      cause
            ANDI     8
            JEQ      handled
            LDA      fall_cnt
            CMPI     0
            JEQ      beat_das
            SUBI     1
            STA      fall_cnt
beat_das:   LDA      das_cnt
            CMPI     0
            JEQ      beat_soft
            SUBI     1
            STA      das_cnt
beat_soft:  LDA      soft_cnt
            CMPI     0
            JEQ      handled
            SUBI     1
            STA      soft_cnt
handled:    POB
            POA
            RTI

; ---- Moving the piece ----
; The piece in play is p_piece, p_rot, p_x, p_y; a move tries t_ first, and try_move makes it the piece if it fits.

move_left:  CALL     p_to_t
            LDA      t_x
            SUBI     1
            STA      t_x
            JMP      try_move
move_right: CALL     p_to_t
            LDA      t_x
            ADDI     1
            STA      t_x
            JMP      try_move

; rotate: a quarter turn clockwise; against a wall or a block it tries one square left, then one right.
rotate:     CALL     turned
            CALL     try_move
            CMPI     0
            JNE      rotated
            CALL     turned
            LDA      t_x
            SUBI     1
            STA      t_x
            CALL     try_move
            CMPI     0
            JNE      rotated
            CALL     turned
            LDA      t_x
            ADDI     1
            STA      t_x
            CALL     try_move
rotated:    RET
turned:     CALL     p_to_t
            LDA      t_rot
            INA
            ANDI     3
            STA      t_rot
            RET

; step_down: one row down, or the piece lands.
step_down:  CALL     p_to_t
            LDA      t_y
            INA
            STA      t_y
            CALL     try_move
            CMPI     0
            JNE      stepped
            CALL     land
stepped:    RET

; hard_drop: finds the lowest row the piece fits in without drawing on the way, moves it there and lands it.
hard_drop:  CALL     p_to_t
hd_lower:   LDA      t_y
            INA
            STA      t_y
            CALL     cells_of
            CALL     fits
            CMPI     0
            JNE      hd_lower
            LDA      t_y
            SUBI     1
            STA      t_y
            CALL     try_move
            JMP      land

; try_move: A = 1 and the piece moved to t_ if it fits there, otherwise A = 0 and nothing changed.
try_move:   CALL     cells_of
            CALL     fits
            CMPI     0
            JEQ      tm_no
            LDA      t_piece     ; keep the new place in n_
            STA      n_piece
            LDA      t_rot
            STA      n_rot
            LDA      t_x
            STA      n_x
            LDA      t_y
            STA      n_y
            CALL     p_to_t      ; rub out the piece where it was
            CALL     cells_of
            LDI      0
            STA      c_color
            CALL     draw_cells
            LDA      n_piece     ; and draw it where it is now
            STA      p_piece
            LDA      n_rot
            STA      p_rot
            LDA      n_x
            STA      p_x
            LDA      n_y
            STA      p_y
            CALL     draw_piece
            LDI      1
            RET
tm_no:      LDI      0
            RET

; draw_piece: draws the piece in play in its colour.
draw_piece: CALL     p_to_t
            CALL     cells_of
            LBA      colors
            LDA      p_piece
            ADD
            TAB
            LDX
            STA      c_color
            JMP      draw_cells

p_to_t:     LDA      p_piece
            STA      t_piece
            LDA      p_rot
            STA      t_rot
            LDA      p_x
            STA      t_x
            LDA      p_y
            STA      t_y
            RET

; ---- A new piece, landing and full rows ----

; spawn: the next piece comes in at the top; if there is no room for it, the game is over.
spawn:      LDA      next
            STA      p_piece
            CALL     random7
            STA      next
            LDI      0
            STA      p_rot
            STA      p_y
            LDI      3
            STA      p_x
            CALL     p_to_t
            CALL     cells_of
            CALL     fits
            CMPI     0
            JNE      sp_room
            LDI      1
            STA      over
            RET
sp_room:    CALL     draw_piece
            CALL     preview
            LDA      fall_delay
            STA      fall_cnt
            RET

; land: the piece becomes part of the well, full rows go, and the next piece comes in.
land:       CALL     lock
            CALL     clear_rows
            CMPI     0
            JEQ      ld_next
            CALL     score_rows
            CALL     redraw
            CALL     show
ld_next:    JMP      spawn

; lock: writes the piece's colour into the well's memory.
lock:       CALL     p_to_t
            CALL     cells_of
            LBA      colors
            LDA      p_piece
            ADD
            TAB
            LDX
            STA      c_color
            LBA      cells
            TBA
            STA      ptr
            LDI      4
            STA      count
lk_cell:    LDA      ptr
            TAB
            LDX
            STA      c_col
            INB
            LDX
            STA      c_row
            INB
            TBA
            STA      ptr
            LDA      top         ; a square above the stack's top is its new top
            TAB
            LDA      c_row
            SUB
            JCC      lk_below
            LDA      c_row
            STA      top
lk_below:   CALL     well_addr
            TAB
            LDA      c_color
            STX
            TBA                  ; the screen already shows it
            ADDI     200
            TAB
            LDA      c_color
            STX
            LDA      count
            SUBI     1
            STA      count
            JNE      lk_cell
            RET

; clear_rows: A = the number of full rows, which are taken out; the rows above move down.
clear_rows: LDI      0
            STA      cleared
            LDA      top         ; redraw starts at the stack's top as it is now
            STA      rd_from
            LDI      19
            STA      row
cr_check:   LDA      top         ; rows above the stack's top are empty
            TAB
            LDA      row
            SUB
            JCS      cr_done
            LDI      0
            STA      c_col
            LDA      row
            STA      c_row
            CALL     well_addr
            STA      ptr
            LDI      10
            STA      count
cr_scan:    LDA      ptr
            TAB
            LDX
            CMPI     0
            JEQ      cr_next
            INB
            TBA
            STA      ptr
            LDA      count
            SUBI     1
            STA      count
            JNE      cr_scan
            LDA      ptr         ; full: each square from the row's last up to the stack's second row takes the one above
            SUBI     1
            STA      dst
            LDA      top
            STA      c_row
            CALL     well_addr
            ADDI     10
            STA      lim
cr_shift:   LDA      lim
            TAB
            LDA      dst
            SUB
            JCS      cr_empty
            LDA      dst
            SUBI     10
            TAB
            LDX
            STA      tmp
            LDA      dst
            TAB
            LDA      tmp
            STX
            LDA      dst
            SUBI     1
            STA      dst
            JMP      cr_shift
cr_empty:   LDA      top         ; and the stack's top row is empty now
            STA      c_row
            CALL     well_addr
            TAB
            LDI      10
            STA      count
cr_zero:    LDI      0
            STX
            INB
            LDA      count
            SUBI     1
            STA      count
            JNE      cr_zero
            LDA      top
            INA
            STA      top
            LDA      cleared
            INA
            STA      cleared
            JMP      cr_check    ; the same row again: it holds what was above
cr_next:    LDA      row
            CMPI     0
            JEQ      cr_done
            SUBI     1
            STA      row
            JMP      cr_check
cr_done:    LDA      cleared
            RET

; score_rows: A full rows score 100, 300, 500 or 800, and count towards the next level.
score_rows: STA      cleared
            LBA      points
            ADD
            TAB
            LDX
            STA      count
sr_point:   LBA      score_100
            CALL     bump
            LDA      count
            SUBI     1
            STA      count
            JNE      sr_point
sr_row:     LBA      lines_1
            CALL     bump
            LDA      lines_1
            CMPI     '0'
            JNE      sr_more
            LDA      level       ; ten more rows: the next level, up to 9
            CMPI     '9'
            JEQ      sr_more
            INA
            STA      level
            SUBI     '0'
            LBA      delays
            ADD
            TAB
            LDX
            STA      fall_delay
sr_more:    LDA      cleared
            SUBI     1
            STA      cleared
            JNE      sr_row
            RET

; bump: adds one to the decimal digit at B, carrying into the digits before it.
bump:       LDX
            INA
            STX
            CMPI     ':'         ; past '9'
            JNE      bumped
            LDI      '0'
            STX
            DEB
            JMP      bump
bumped:     RET

; zeros: count digits from B on become '0'.
zeros:      LDI      '0'
            STX
            INB
            LDA      count
            SUBI     1
            STA      count
            JNE      zeros
            RET

; ---- Where the piece's squares are ----

; cells_of: the four squares of piece t_ in the well, as column and row pairs in cells.
cells_of:   LBA      bases       ; the piece's shapes start at shapes + 32 x piece, each turn 8 cells on
            LDA      t_piece
            ADD
            TAB
            LDX
            STA      tmp
            LDA      t_rot
            TAB
            ADD
            TAB
            ADD
            TAB
            ADD                  ; turn x 8
            TAB
            LDA      tmp
            ADD
            STA      tmp
            LBA      shapes
            LDA      tmp
            ADD
            STA      ptr
            LBA      cells
            TBA
            STA      optr
            LDI      4
            STA      count
co_cell:    LDA      ptr         ; column = x + the square's column in the shape
            TAB
            LDX
            STA      tmp
            LDA      t_x
            TAB
            LDA      tmp
            ADD
            STA      tmp
            LDA      optr
            TAB
            LDA      tmp
            STX
            LDA      ptr         ; row = y + the square's row in the shape
            INA
            TAB
            LDX
            STA      tmp
            LDA      t_y
            TAB
            LDA      tmp
            ADD
            STA      tmp
            LDA      optr
            INA
            TAB
            LDA      tmp
            STX
            LDA      ptr
            ADDI     2
            STA      ptr
            LDA      optr
            ADDI     2
            STA      optr
            LDA      count
            SUBI     1
            STA      count
            JNE      co_cell
            RET

; fits: A = 1 if all four squares in cells are inside the well and empty. A column left of the wall wraps
; round to a large number, so one unsigned test checks both walls.
fits:       LBA      cells
            TBA
            STA      ptr
            LDI      4
            STA      count
fi_cell:    LDA      ptr
            TAB
            LDX
            STA      c_col
            CMPI     10
            JCC      no_fit
            INB
            LDX
            STA      c_row
            CMPI     20
            JCC      no_fit
            INB
            TBA
            STA      ptr
            CALL     well_addr
            TAB
            LDX
            CMPI     0
            JNE      no_fit
            LDA      count
            SUBI     1
            STA      count
            JNE      fi_cell
            LDI      1
            RET
no_fit:     LDI      0
            RET

; well_addr: A = the address of the square at c_col, c_row.
well_addr:  LBA      row_base
            LDA      c_row
            ADD
            TAB
            LDX
            STA      tmp
            LDA      c_col
            TAB
            LDA      tmp
            ADD
            ADDI     3500
            RET

; ---- Drawing ----

; draw_cells: draws the four squares in cells in c_color.
draw_cells: LBA      cells
            TBA
            STA      dptr
            LDI      4
            STA      dcount
dc_cell:    LDA      dptr
            TAB
            LDX
            STA      c_col
            INB
            LDX
            STA      c_row
            INB
            TBA
            STA      dptr
            CALL     draw_cell
            LDA      dcount
            SUBI     1
            STA      dcount
            JNE      dc_cell
            RET

; draw_cell: one square of the well, c_col, c_row, in c_color, leaving a black line between squares.
draw_cell:  LBA      x_of
            LDA      c_col
            ADD
            TAB
            LDX
            STA      r_x
            LBA      y_of
            LDA      c_row
            ADD
            TAB
            LDX
            STA      r_y
            LDI      19
            STA      r_w
            STA      r_h
            LDA      c_color
            STA      r_c
; draw_rect: hands the blitter r_x, r_y, r_w, r_h and r_c, once it has finished the last one.
draw_rect:  LDA      blit_busy
            CMPI     0
            JNE      draw_rect
            LDA      r_x
            BX
            LDA      r_y
            BY
            LDA      r_w
            BW
            LDA      r_h
            BH
            LDA      r_c
            BC
            LDI      1
            STA      blit_busy
            GO
            RET

; redraw: after rows have gone, draws the squares of the well that differ from what the screen shows.
redraw:     LDA      rd_from     ; nothing above where the stack's top was has changed
            STA      c_row
rd_row:     LDI      0
            STA      c_col
rd_cell:    CALL     well_addr
            STA      addr
            TAB
            LDX
            STA      c_color
            LDA      addr        ; what the screen shows there
            ADDI     200
            TAB
            LDX
            STA      tmp
            LDA      c_color
            TAB
            LDA      tmp
            SUB
            JEQ      rd_same
            LDA      addr
            ADDI     200
            TAB
            LDA      c_color
            STX
            CALL     draw_cell
rd_same:    LDA      c_col
            INA
            STA      c_col
            CMPI     10
            JNE      rd_cell
            LDA      c_row
            INA
            STA      c_row
            CMPI     20
            JNE      rd_row
            RET

; preview: the next piece, in the box to the right of the well.
preview:    LDI      455
            STA      r_x
            LDI      75
            STA      r_y
            LDI      90
            STA      r_w
            LDI      70
            STA      r_h
            LDI      0
            STA      r_c
            CALL     draw_rect
            LBA      colors
            LDA      next
            ADD
            TAB
            LDX
            STA      r_c
            LBA      bases
            LDA      next
            ADD
            TAB
            LDX
            STA      tmp
            LBA      shapes
            LDA      tmp
            ADD
            STA      dptr
            LDI      4
            STA      dcount
pv_cell:    LDA      dptr
            TAB
            LDX
            STA      tmp
            LBA      px_of
            LDA      tmp
            ADD
            TAB
            LDX
            STA      r_x
            LDA      dptr
            INA
            TAB
            LDX
            STA      tmp
            LBA      py_of
            LDA      tmp
            ADD
            TAB
            LDX
            STA      r_y
            LDI      19
            STA      r_w
            STA      r_h
            CALL     draw_rect
            LDA      dptr
            ADDI     2
            STA      dptr
            LDA      dcount
            SUBI     1
            STA      dcount
            JNE      pv_cell
            RET

; frame: grey borders round the well and the preview box.
frame:      LDI      0x8410
            STA      r_c
            LDI      210
            STA      r_x
            LDI      30
            STA      r_y
            LDI      220
            STA      r_w
            LDI      420
            STA      r_h
            CALL     draw_rect
            LDI      445
            STA      r_x
            LDI      65
            STA      r_y
            LDI      110
            STA      r_w
            LDI      90
            STA      r_h
            CALL     draw_rect
            LDI      0
            STA      r_c
            LDI      220
            STA      r_x
            LDI      40
            STA      r_y
            LDI      200
            STA      r_w
            LDI      400
            STA      r_h
            CALL     draw_rect
            LDI      455
            STA      r_x
            LDI      75
            STA      r_y
            LDI      90
            STA      r_w
            LDI      70
            STA      r_h
            JMP      draw_rect

; ---- The LCD ----

; show: score, rows and level, then the keys, or that the game is over.
show:       CLT
            LBA      t_score
            CALL     print
            LBA      score
            INB                  ; past the hidden digit
            LDI      6
            STA      count
            CALL     digits
            LBA      t_lines
            CALL     print
            LBA      lines
            INB
            LDI      3
            STA      count
            CALL     digits
            LBA      t_level
            CALL     print
            LDA      level
            OUT
            LDA      over
            CMPI     0
            JNE      sh_over
            LBA      t_keys
            JMP      print
sh_over:    LBA      t_over
            JMP      print

; digits: prints count characters from B.
digits:     LDX
            OUT
            INB
            LDA      count
            SUBI     1
            STA      count
            JNE      digits
            RET

; print: prints the zero terminated string at B.
print:      LDX
            CMPI     0
            JEQ      printed
            OUT
            INB
            JMP      print
printed:    RET

; ---- Random pieces ----

; random7: A = a piece, 0 to 6, from four steps of a 16 bit shift register (a Galois LFSR).
random7:    CALL     random
            CALL     random
            CALL     random
            CALL     random
            ANDI     7
            CMPI     7
            JEQ      random7
            RET
random:     LDA      seed
            ANDI     1
            STA      tmp
            LDA      seed
            LSRI     1
            STA      seed
            LDA      tmp
            CMPI     0
            JEQ      rn_done
            LDA      seed
            EORI     0xB400
            STA      seed
rn_done:    LDA      seed
            RET

; ---- Tables ----

; The seven pieces, I O T S Z J L, each in four turns of four squares: column, row in a 4 x 4 box.
shapes:     .DATA    0, 1, 1, 1, 2, 1, 3, 1, 2, 0, 2, 1, 2, 2, 2, 3, 0, 2, 1, 2, 2, 2, 3, 2, 1, 0, 1, 1, 1, 2, 1, 3
            .DATA    1, 0, 2, 0, 1, 1, 2, 1, 1, 0, 2, 0, 1, 1, 2, 1, 1, 0, 2, 0, 1, 1, 2, 1, 1, 0, 2, 0, 1, 1, 2, 1
            .DATA    1, 0, 0, 1, 1, 1, 2, 1, 1, 0, 1, 1, 2, 1, 1, 2, 0, 1, 1, 1, 2, 1, 1, 2, 1, 0, 0, 1, 1, 1, 1, 2
            .DATA    1, 0, 2, 0, 0, 1, 1, 1, 1, 0, 1, 1, 2, 1, 2, 2, 1, 1, 2, 1, 0, 2, 1, 2, 0, 0, 0, 1, 1, 1, 1, 2
            .DATA    0, 0, 1, 0, 1, 1, 2, 1, 2, 0, 1, 1, 2, 1, 1, 2, 0, 1, 1, 1, 1, 2, 2, 2, 1, 0, 0, 1, 1, 1, 0, 2
            .DATA    0, 0, 0, 1, 1, 1, 2, 1, 1, 0, 2, 0, 1, 1, 1, 2, 0, 1, 1, 1, 2, 1, 2, 2, 1, 0, 1, 1, 0, 2, 1, 2
            .DATA    2, 0, 0, 1, 1, 1, 2, 1, 1, 0, 1, 1, 1, 2, 2, 2, 0, 1, 1, 1, 2, 1, 0, 2, 0, 0, 1, 0, 1, 1, 1, 2
bases:      .DATA    0, 32, 64, 96, 128, 160, 192
colors:     .DATA    0x07FF, 0xFFE0, 0xA01F, 0x07E0, 0xF800, 0x3A7F, 0xFC00
row_base:   .DATA    0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190
x_of:       .DATA    220, 240, 260, 280, 300, 320, 340, 360, 380, 400
y_of:       .DATA    40, 60, 80, 100, 120, 140, 160, 180, 200, 220, 240, 260, 280, 300, 320, 340, 360, 380, 400, 420
px_of:      .DATA    460, 480, 500, 520
py_of:      .DATA    80, 100, 120, 140
; Hundreds scored for 1, 2, 3 and 4 rows at once.
points:     .DATA    0, 1, 3, 5, 8
; Beats between falls, by level: from 0.8 seconds down to 0.1.
delays:     .DATA    50, 45, 39, 34, 29, 24, 19, 14, 8, 6

t_title:    .STRING  "FALLING BLOCKS", 10, "Space to start", 10, "Arrows move, up turns,", 10, "down falls faster, space drops"
t_score:    .STRING  "SCORE "
t_lines:    .STRING  "   ROWS "
t_level:    .STRING  10, "LEVEL "
t_keys:     .STRING  10, "Arrows move, up turns,", 10, "down falls faster, space drops"
t_over:     .STRING  10, "GAME OVER", 10, "Space plays again"

; The score and rows as decimal digits, each after a hidden digit that takes any carry out of the top.
score:      .DATA    '0', '0', '0', '0'
score_100:  .DATA    '0', '0', '0'
lines:      .DATA    '0', '0', '0'
lines_1:    .DATA    '0'
level:      .DATA    '0'

; The piece in play, the one being tried, and the new place try_move keeps.
p_piece:    .DATA    0
p_rot:      .DATA    0
p_x:        .DATA    0
p_y:        .DATA    0
t_piece:    .DATA    0
t_rot:      .DATA    0
t_x:        .DATA    0
t_y:        .DATA    0
n_piece:    .DATA    0
n_rot:      .DATA    0
n_x:        .DATA    0
n_y:        .DATA    0
next:       .DATA    0
cells:      .DATA    0, 0, 0, 0, 0, 0, 0, 0
; Counted down by the real time clock's beat.
fall_cnt:   .DATA    0
das_cnt:    .DATA    0
soft_cnt:   .DATA    0
fall_delay: .DATA    0
blit_busy:  .DATA    0
seed:       .DATA    0
over:       .DATA    0
keys:       .DATA    0
prev:       .DATA    0
newk:       .DATA    0
cause:      .DATA    0
count:      .DATA    0
ptr:        .DATA    0
optr:       .DATA    0
dptr:       .DATA    0
dcount:     .DATA    0
row:        .DATA    0
dst:        .DATA    0
addr:       .DATA    0
top:        .DATA    0
rd_from:    .DATA    0
lim:        .DATA    0
cleared:    .DATA    0
tmp:        .DATA    0
c_col:      .DATA    0
c_row:      .DATA    0
c_color:    .DATA    0
r_x:        .DATA    0
r_y:        .DATA    0
r_w:        .DATA    0
r_h:        .DATA    0
r_c:        .DATA    0
