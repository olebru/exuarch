; Two triangles through each other
; The red triangle is near on the left and far on the right, the blue one the other way round. The depth
; buffer keeps the nearer pixel, so they cut through each other along a line no triangle has. Pixels hidden
; behind the other triangle cost a depth read but no write.
          CLS
          ZCLR
          LBA    tris
          TBA
          STA    src
          LDI    0
          STA    dst
          LDI    24
          STA    n
          CALL   copy                   ; the triangles into the list, 24 words
          LDI    0
          GADR                          ; they start at 0 in the list
          LDI    2
          GCNT
          GO                            ; the rasterizer draws them, without the CPU
          CALL   wait
          HLT
; copy: n words from main memory at src to the triangle list at dst. The list is on its own bus, so every word
; goes through the bridge (STL): the rasterizer must be idle.
copy:     LDA    n
          CMPI   0
          JEQ    copy_end
          LDA    src
          TAB
          LDX                           ; A = the word at src
          PSA
          LDA    dst
          TAB
          POA
          STL                           ; the list at dst = A
          LDA    src
          INA
          STA    src
          LDA    dst
          INA
          STA    dst
          LDA    n
          SUBI   1
          STA    n
          JMP    copy
copy_end: RET
; wait: until the rasterizer is idle
wait:     GST
          CMPI   0
          JNE    wait
          RET
src:      .DATA  0
dst:      .DATA  0
n:        .DATA  0
tris:     .DATA  80, 100, 2000, 0xF800  ; x, y, z, colour for each corner
          .DATA  560, 160, 30000, 0xF800
          .DATA  120, 420, 2000, 0xF800
          .DATA  560, 100, 2000, 0x001F
          .DATA  80, 180, 30000, 0x001F
          .DATA  520, 420, 2000, 0x001F
