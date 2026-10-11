; One flat triangle
; The CPU copies one triangle, 12 words, into the rasterizer's list and starts it. All three corners have the
; same colour, so the triangle is flat. Watch the trace: the rasterizer reads the 12 words over the list bus,
; then fills row after row on the video bus while the CPU waits.
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
