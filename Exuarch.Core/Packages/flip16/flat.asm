; A triangle drawn out of sight
; GPU-16's flat triangle, drawn in the back buffer. Step it: the trace shows the rasterizer plotting row after row,
; but the screen stays black, until SWAP shows the whole triangle in one tick.
          CLS
          LBA    tris
          TBA
          STA    src
          LDI    0
          STA    dst
          LDI    12
          STA    n
          CALL   copy                 ; the triangles into the list, 12 words
          LDI    0
          GADR                        ; they start at 0 in the list
          LDI    1
          GCNT
          GO                          ; the rasterizer draws them, without the CPU
          CALL   wait
          SWAP                        ; the finished triangle appears at once
          HLT
; copy: n words from main memory at src to the triangle list at dst. The list is on its own bus, so every word
; goes through the bridge (STL): the rasterizer must be idle.
copy:     LDA    n
          CMPI   0
          JEQ    copy_end
          LDA    src
          TAB
          LDX                         ; A = the word at src
          PSA
          LDA    dst
          TAB
          POA
          STL                         ; the list at dst = A
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
tris:     .DATA  160, 400, 0, 0xFEE0  ; x, y, z, colour for each corner
          .DATA  320, 80, 0, 0xFEE0
          .DATA  480, 400, 0, 0xFEE0
