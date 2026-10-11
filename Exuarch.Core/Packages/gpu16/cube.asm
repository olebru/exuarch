; A spinning cube
; The whole pipeline. Each frame the CPU turns the cube's eight corners round the vertical axis with the
; multiply-accumulate unit (a cosine and a sine from a table, in 8.8 fixed point), projects them onto the screen
; (x * 320 / z, the perspective divide), and works out each corner's depth. Then it waits for the rasterizer to finish
; the last frame, clears the screen and the depth buffer, writes the 12 triangles into the list and starts it.
; While the rasterizer draws, the CPU is already turning the corners for the next frame. The corners are coloured
; like an RGB cube, so the faces are shaded, and the depth buffer hides the back ones.
           LDI    0
           STA    angle
frame:     LBA    cos_table                     ; cosv = cos_table[angle], sinv = sin_table[angle], nsinv = -sinv
           LDA    angle
           ADD
           TAB
           LDX
           STA    cosv
           LBA    sin_table
           LDA    angle
           ADD
           TAB
           LDX
           STA    sinv
           TAB
           LDI    0
           SUB
           STA    nsinv
           CALL   turn                          ; the corners, turned and projected, into proj
           CALL   wait                          ; the last frame is drawn
           CLS
           ZCLR
           CALL   build                         ; the 12 triangles into the list
           LDI    0
           GADR
           LDI    12
           GCNT
           GO
           LDA    angle                         ; the next angle, round and round
           INA
           ANDI   63
           STA    angle
           JMP    frame

; turn: every corner (x, y, z) in verts becomes (screen x, screen y, depth) in proj.
turn:      LBA    verts
           TBA
           STA    p
           LBA    proj
           TBA
           STA    q
           LDI    8
           STA    count
corner:    LDA    p                             ; x, y, z of this corner
           TAB
           LDX
           STA    vx
           INB
           LDX
           STA    vy
           INB
           LDX
           STA    vz
           INB
           TBA
           STA    p
           LDA    vx                            ; xr = x * cos + z * sin
           MA
           LDA    cosv
           MB
           MUL
           LDA    vz
           MA
           LDA    sinv
           MB
           MAC
           MRD
           STA    xr
           LDA    vz                            ; zr = z * cos - x * sin, then away from the camera
           MA
           LDA    cosv
           MB
           MUL
           LDA    vx
           MA
           LDA    nsinv
           MB
           MAC
           MRD
           ADDI   240
           STA    zc
           LDA    xr                            ; screen x = 320 + xr * 320 / zc
           MA
           MBI    320
           MUL
           LDA    zc
           MB
           MDIV
           MRD
           ADDI   320
           CALL   keep
           LDA    vy                            ; screen y = 240 - y * 320 / zc
           MA
           MBI    320
           MUL
           LDA    zc
           MB
           MDIV
           MRD
           TAB
           LDI    240
           SUB
           CALL   keep
           LDA    zc                            ; depth = zc * 64
           MA
           MBI    16384
           MUL
           MRD
           CALL   keep
           LDA    count
           SUBI   1
           STA    count
           JNE    corner
           RET

; keep: A into proj at q, and move q on
keep:      PSA
           LDA    q
           TAB
           POA
           STX
           INB
           TBA
           STA    q
           RET

; build: for each corner of each face, its screen x, y, depth and colour into the list
build:     LBA    faces
           TBA
           STA    f
           LDI    0
           STA    d
           LDI    36
           STA    count
bcorner:   LDA    f                             ; the corner's offset in proj, and its colour
           TAB
           LDX
           STA    off
           INB
           LDX
           STA    col
           INB
           TBA
           STA    f
           LBA    proj
           LDA    off
           ADD
           STA    vp
           TAB
           LDX
           CALL   put                           ; screen x
           LDA    vp
           INA
           TAB
           LDX
           CALL   put                           ; screen y
           LDA    vp
           ADDI   2
           TAB
           LDX
           CALL   put                           ; depth
           LDA    col
           CALL   put                           ; colour
           LDA    count
           SUBI   1
           STA    count
           JNE    bcorner
           RET

; put: A into the list at d, and move d on
put:       PSA
           LDA    d
           TAB
           POA
           STL
           LDA    d
           INA
           STA    d
           RET

; wait: until the rasterizer is idle
wait:      GST
           CMPI   0
           JNE    wait
           RET

angle:     .DATA  0
cosv:      .DATA  0
sinv:      .DATA  0
nsinv:     .DATA  0
p:         .DATA  0
q:         .DATA  0
f:         .DATA  0
d:         .DATA  0
count:     .DATA  0
vx:        .DATA  0
vy:        .DATA  0
vz:        .DATA  0
xr:        .DATA  0
zc:        .DATA  0
off:       .DATA  0
col:       .DATA  0
vp:        .DATA  0
proj:      .DATA  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
           .DATA  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
; Negative numbers are written as 65536 minus the number: 65486 is -50, the way 16 bit two's complement holds it.
verts:     .DATA  65499, 65504, 65475           ; x, y, z of the eight corners, tilted a little
           .DATA  65512, 65464, 19
           .DATA  65474, 44, 65517
           .DATA  65487, 4, 61
           .DATA  49, 65532, 65475
           .DATA  62, 65492, 19
           .DATA  24, 72, 65517
           .DATA  37, 32, 61
faces:     .DATA  0, 0x2945, 3, 0x295F, 9, 0x2FFF ; per corner of each face: its offset in proj and its colour
           .DATA  0, 0x2945, 9, 0x2FFF, 6, 0x2FE5
           .DATA  12, 0xF945, 18, 0xFFE5, 21, 0xFFFF
           .DATA  12, 0xF945, 21, 0xFFFF, 15, 0xF95F
           .DATA  0, 0x2945, 12, 0xF945, 15, 0xF95F
           .DATA  0, 0x2945, 15, 0xF95F, 3, 0x295F
           .DATA  6, 0x2FE5, 9, 0x2FFF, 21, 0xFFFF
           .DATA  6, 0x2FE5, 21, 0xFFFF, 18, 0xFFE5
           .DATA  0, 0x2945, 6, 0x2FE5, 18, 0xFFE5
           .DATA  0, 0x2945, 18, 0xFFE5, 12, 0xF945
           .DATA  3, 0x295F, 15, 0xF95F, 21, 0xFFFF
           .DATA  3, 0x295F, 21, 0xFFFF, 9, 0x2FFF
cos_table: .DATA  256, 255, 251, 245, 237, 226, 213, 198 ; 256 * cos, 64 steps round
           .DATA  181, 162, 142, 121, 98, 74, 50, 25
           .DATA  0, 65511, 65486, 65462, 65438, 65415, 65394, 65374
           .DATA  65355, 65338, 65323, 65310, 65299, 65291, 65285, 65281
           .DATA  65280, 65281, 65285, 65291, 65299, 65310, 65323, 65338
           .DATA  65355, 65374, 65394, 65415, 65438, 65462, 65486, 65511
           .DATA  0, 25, 50, 74, 98, 121, 142, 162
           .DATA  181, 198, 213, 226, 237, 245, 251, 255
sin_table: .DATA  0, 25, 50, 74, 98, 121, 142, 162
           .DATA  181, 198, 213, 226, 237, 245, 251, 255
           .DATA  256, 255, 251, 245, 237, 226, 213, 198
           .DATA  181, 162, 142, 121, 98, 74, 50, 25
           .DATA  0, 65511, 65486, 65462, 65438, 65415, 65394, 65374
           .DATA  65355, 65338, 65323, 65310, 65299, 65291, 65285, 65281
           .DATA  65280, 65281, 65285, 65291, 65299, 65310, 65323, 65338
           .DATA  65355, 65374, 65394, 65415, 65438, 65462, 65486, 65511
