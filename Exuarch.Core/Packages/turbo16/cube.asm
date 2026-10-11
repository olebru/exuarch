; A spinning cube, as fast as TURBO-16 can draw it
; Lap one: each of the 64 angles is turned, projected and culled while the rasterizer draws the angle before, and only
; the triangles that face the camera are kept. They are copied into the triangle list, one block per angle, and drawn.
; Every lap after that is a replay: wait for the rasterizer, SWAP, CLS, point it at the next block and GO.
           LDI     0
           ALP
           STA     angle
lap:       LBA     cos_table                    ; the first lap: work out each angle while the last one is drawn
           LDA     angle
           ADD
           TAB
           LDX
           STA     cosv
           LBA     sin_table
           LDA     angle
           ADD
           TAB
           LDX
           STA     sinv
           TAB
           LDI     0
           SUB
           STA     nsinv
           CALL    turn
           CALL    build
wait:      GST
           CMPI    0
           JNE     wait
           SWAP
           CLS
           LBA     starts                       ; starts[angle] = where this angle's block begins
           LDA     angle
           ADD
           TAB
           LPA
           STX
           STA     start
           LBA     counts                       ; counts[angle] = how many triangles are in it
           LDA     angle
           ADD
           TAB
           LDA     ntri
           STX
           STA     count
           LBA     tris
           TBA
           AMP
copy:      COPYT
           LDA     count
           SUBI    1
           STA     count
           JNE     copy
           LDA     start
           GADR
           LDA     ntri
           GCNT
           GO
           LDA     angle
           INA
           STA     angle
           CMPI    64
           JNE     lap
           LDI     0                            ; then replay: mp and lp hold the next block and its size
           STA     angle
           LBA     starts
           LDX
           AMP
           LBA     counts
           LDX
           ALP
play:      WAITGO                               ; wait, SWAP, CLS, GO
           LDA     angle
           INA
           ANDI    63
           STA     angle
           LBA     starts
           ADD
           TAB
           LDX
           AMP
           LBA     counts
           LDA     angle
           ADD
           TAB
           LDX
           ALP
           JMP     play

; build: the triangles of the faces that point at the camera, into tris; ntri holds the count
build:     LBA     tris
           TBA
           AMP
           LBA     faces
           TBA
           STA     f
           LDI     0
           STA     ntri
           LDI     12
           STA     count
btri:      LDA     f                            ; three corners: offset in proj and colour
           TAB
           LDX
           STA     o0
           INB
           LDX
           STA     c0
           INB
           LDX
           STA     o1
           INB
           LDX
           STA     c1
           INB
           LDX
           STA     o2
           INB
           LDX
           STA     c2
           INB
           TBA
           STA     f
           LBA     proj
           LDA     o0
           ADD
           TAB
           LDX
           STA     x0
           INB
           LDX
           STA     y0
           INB
           LDX
           STA     z0
           LBA     proj
           LDA     o1
           ADD
           TAB
           LDX
           STA     x1
           INB
           LDX
           STA     y1
           INB
           LDX
           STA     z1
           LBA     proj
           LDA     o2
           ADD
           TAB
           LDX
           STA     x2
           INB
           LDX
           STA     y2
           INB
           LDX
           STA     z2
           LDA     x0                           ; (x1 - x0)(y2 - y0) - (y1 - y0)(x2 - x0), the rasterizer's own area
           TAB
           LDA     x1
           SUB
           XA
           LDA     y0
           TAB
           LDA     y2
           SUB
           XB
           XMUL
           LDA     y1
           TAB
           LDA     y0
           SUB
           XA
           LDA     x0
           TAB
           LDA     x2
           SUB
           XB
           XMAC
           XRD
           CMPI    0
           JEQ     bnext
           JMI     bnext
           LDA     x0
           PUTM
           LDA     y0
           PUTM
           LDA     z0
           PUTM
           LDA     c0
           PUTM
           LDA     x1
           PUTM
           LDA     y1
           PUTM
           LDA     z1
           PUTM
           LDA     c1
           PUTM
           LDA     x2
           PUTM
           LDA     y2
           PUTM
           LDA     z2
           PUTM
           LDA     c2
           PUTM
           LDA     ntri
           INA
           STA     ntri
bnext:     LDA     count
           SUBI    1
           STA     count
           JNE     btri
           RET

; turn: every corner (x, y, z) in verts becomes (screen x, screen y, depth) in proj.
turn:      LBA     verts
           TBA
           STA     p
           LBA     proj
           TBA
           STA     q
           LDI     8
           STA     count
corner:    LDA     p                            ; x, y, z of this corner
           TAB
           LDX
           STA     vx
           INB
           LDX
           STA     vy
           INB
           LDX
           STA     vz
           INB
           TBA
           STA     p
           LDA     vx                           ; xr = x * cos + z * sin
           MA
           LDA     cosv
           MB
           MUL
           LDA     vz
           MA
           LDA     sinv
           MB
           MAC
           MRD
           STA     xr
           LDA     vz                           ; zr = z * cos - x * sin, then away from the camera
           MA
           LDA     cosv
           MB
           MUL
           LDA     vx
           MA
           LDA     nsinv
           MB
           MAC
           MRD
           ADDI    240
           STA     zc
           LDA     xr                           ; screen x = 320 + xr * 320 / zc
           MA
           MBI     320
           MUL
           LDA     zc
           MB
           MDIV
           MRD
           ADDI    320
           CALL    keep
           LDA     vy                           ; screen y = 240 - y * 320 / zc
           MA
           MBI     320
           MUL
           LDA     zc
           MB
           MDIV
           MRD
           TAB
           LDI     240
           SUB
           CALL    keep
           LDA     zc                           ; depth = zc * 64
           MA
           MBI     16384
           MUL
           MRD
           CALL    keep
           LDA     count
           SUBI    1
           STA     count
           JNE     corner
           RET

; keep: A into proj at q, and move q on
keep:      PSA
           LDA     q
           TAB
           POA
           STX
           INB
           TBA
           STA     q
           RET

angle:     .DATA   0
cosv:      .DATA   0
sinv:      .DATA   0
nsinv:     .DATA   0
p:         .DATA   0
q:         .DATA   0
f:         .DATA   0
count:     .DATA   0
ntri:      .DATA   0
start:     .DATA   0
vx:        .DATA   0
vy:        .DATA   0
vz:        .DATA   0
xr:        .DATA   0
zc:        .DATA   0
o0:        .DATA   0
o1:        .DATA   0
o2:        .DATA   0
c0:        .DATA   0
c1:        .DATA   0
c2:        .DATA   0
x0:        .DATA   0
y0:        .DATA   0
z0:        .DATA   0
x1:        .DATA   0
y1:        .DATA   0
z1:        .DATA   0
x2:        .DATA   0
y2:        .DATA   0
z2:        .DATA   0
proj:      .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
tris:      .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
starts:    .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
counts:    .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA   0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
verts:     .DATA   65499, 65504, 65475          ; x, y, z of the eight corners, tilted a little
           .DATA   65512, 65464, 19
           .DATA   65474, 44, 65517
           .DATA   65487, 4, 61
           .DATA   49, 65532, 65475
           .DATA   62, 65492, 19
           .DATA   24, 72, 65517
           .DATA   37, 32, 61
faces:     .DATA   0, 0x2945, 3, 0x295F, 9, 0x2FFF ; per corner of each face: its offset in proj and its colour
           .DATA   0, 0x2945, 9, 0x2FFF, 6, 0x2FE5
           .DATA   12, 0xF945, 18, 0xFFE5, 21, 0xFFFF
           .DATA   12, 0xF945, 21, 0xFFFF, 15, 0xF95F
           .DATA   0, 0x2945, 12, 0xF945, 15, 0xF95F
           .DATA   0, 0x2945, 15, 0xF95F, 3, 0x295F
           .DATA   6, 0x2FE5, 9, 0x2FFF, 21, 0xFFFF
           .DATA   6, 0x2FE5, 21, 0xFFFF, 18, 0xFFE5
           .DATA   0, 0x2945, 6, 0x2FE5, 18, 0xFFE5
           .DATA   0, 0x2945, 18, 0xFFE5, 12, 0xF945
           .DATA   3, 0x295F, 15, 0xF95F, 21, 0xFFFF
           .DATA   3, 0x295F, 21, 0xFFFF, 9, 0x2FFF
cos_table: .DATA   256, 255, 251, 245, 237, 226, 213, 198 ; 256 * cos, 64 steps round
           .DATA   181, 162, 142, 121, 98, 74, 50, 25
           .DATA   0, 65511, 65486, 65462, 65438, 65415, 65394, 65374
           .DATA   65355, 65338, 65323, 65310, 65299, 65291, 65285, 65281
           .DATA   65280, 65281, 65285, 65291, 65299, 65310, 65323, 65338
           .DATA   65355, 65374, 65394, 65415, 65438, 65462, 65486, 65511
           .DATA   0, 25, 50, 74, 98, 121, 142, 162
           .DATA   181, 198, 213, 226, 237, 245, 251, 255
sin_table: .DATA   0, 25, 50, 74, 98, 121, 142, 162
           .DATA   181, 198, 213, 226, 237, 245, 251, 255
           .DATA   256, 255, 251, 245, 237, 226, 213, 198
           .DATA   181, 162, 142, 121, 98, 74, 50, 25
           .DATA   0, 65511, 65486, 65462, 65438, 65415, 65394, 65374
           .DATA   65355, 65338, 65323, 65310, 65299, 65291, 65285, 65281
           .DATA   65280, 65281, 65285, 65291, 65299, 65310, 65323, 65338
           .DATA   65355, 65374, 65394, 65415, 65438, 65462, 65486, 65511
