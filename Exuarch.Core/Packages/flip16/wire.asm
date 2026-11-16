; A spinning wireframe cube
; The cube's 12 edges instead of its faces, each in a colour of its own. The corners are turned and projected as in
; the solid cube. The rasterizer only fills triangles, and drops one with no area, so an edge is drawn as a thin quad:
; two triangles along the line between its corners, 2 pixels wide. A line that runs more across than down is widened
; downwards, one that runs more down than across is widened sideways, so every edge looks as thick. Each end has
; its depth, so where two edges cross, the nearer one is in front.
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
           SWAP                                 ; show it
           CLS
           ZCLR
           CALL   edges                         ; the 24 triangles of the 12 edges into the list
           LDI    0
           GADR
           LDI    24
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

; edges: for each edge, two triangles that make a quad 2 pixels wide along it
edges:     LBA    lines
           TBA
           STA    f
           LDI    0
           STA    d
           LDI    12
           STA    count
edge:      LDA    f                             ; its corners' offsets in proj, and its colour
           TAB
           LDX
           STA    off
           INB
           LDX
           STA    off2
           INB
           LDX
           STA    col
           INB
           TBA
           STA    f
           LBA    proj                          ; ax, ay, az: the first corner on the screen
           LDA    off
           ADD
           TAB
           LDX
           STA    ax
           INB
           LDX
           STA    ay
           INB
           LDX
           STA    az
           LBA    proj                          ; bx, by, bz: the second
           LDA    off2
           ADD
           TAB
           LDX
           STA    bx
           INB
           LDX
           STA    by
           INB
           LDX
           STA    bz
           LDA    ax                            ; |bx - ax|
           TAB
           LDA    bx
           SUB
           JPL    across
           TAB
           LDI    0
           SUB
across:    STA    wide
           LDA    ay                            ; |by - ay|
           TAB
           LDA    by
           SUB
           JPL    down
           TAB
           LDI    0
           SUB
down:      TAB
           LDA    wide                          ; more down than across: widen sideways
           SUB
           JMI    steep
           LDI    0
           STA    wx
           LDI    2
           STA    wy
           JMP    quad
steep:     LDI    2
           STA    wx
           LDI    0
           STA    wy
quad:      CALL   at_a                          ; a, b, b moved: the first triangle
           CALL   at_b
           CALL   at_bw
           CALL   at_a                          ; a, b moved, a moved: the second
           CALL   at_bw
           CALL   at_aw
           LDA    count
           SUBI   1
           STA    count
           JNE    edge
           RET

; at_a, at_b, at_aw, at_bw: a corner of the quad into the list, the edge's ends as they are or moved by wx, wy
at_a:      LDA    ax
           STA    px
           LDA    ay
           STA    py
           LDA    az
           JMP    vertex
at_b:      LDA    bx
           STA    px
           LDA    by
           STA    py
           LDA    bz
           JMP    vertex
at_aw:     LDA    wx
           TAB
           LDA    ax
           ADD
           STA    px
           LDA    wy
           TAB
           LDA    ay
           ADD
           STA    py
           LDA    az
           JMP    vertex
at_bw:     LDA    wx
           TAB
           LDA    bx
           ADD
           STA    px
           LDA    wy
           TAB
           LDA    by
           ADD
           STA    py
           LDA    bz
; vertex: px, py, the depth in A and the edge's colour into the list
vertex:    STA    pz
           LDA    px
           CALL   put
           LDA    py
           CALL   put
           LDA    pz
           CALL   put
           LDA    col
           CALL   put
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
off2:      .DATA  0
ax:        .DATA  0
ay:        .DATA  0
az:        .DATA  0
bx:        .DATA  0
by:        .DATA  0
bz:        .DATA  0
wide:      .DATA  0
wx:        .DATA  0
wy:        .DATA  0
px:        .DATA  0
py:        .DATA  0
pz:        .DATA  0
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
lines:     .DATA  0, 3, 0xF924
           .DATA  3, 9, 0xFC84
           .DATA  9, 6, 0xFFE4
           .DATA  6, 0, 0x97E4
           .DATA  12, 18, 0x27E4
           .DATA  18, 21, 0x27F2
           .DATA  21, 15, 0x27FF
           .DATA  15, 12, 0x249F
           .DATA  0, 12, 0x213F
           .DATA  3, 15, 0x913F
           .DATA  6, 18, 0xF93F
           .DATA  9, 21, 0xF932
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