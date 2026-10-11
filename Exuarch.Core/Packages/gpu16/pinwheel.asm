; A pinwheel from one list
; Sixteen triangles in one list and a single GO. While the rasterizer works through them, the CPU is free:
; it prints a dot on the LCD each time it reads the rasterizer as still busy.
          CLS
          ZCLR
          LBA    tris
          TBA
          STA    src
          LDI    0
          STA    dst
          LDI    192
          STA    n
          CALL   copy                    ; the triangles into the list, 192 words
          LDI    0
          GADR                           ; they start at 0 in the list
          LDI    16
          GCNT
          GO                             ; the rasterizer draws them, without the CPU
busy:     GST
          CMPI   0
          JEQ    done
          LDI    '.'
          OUT
          JMP    busy
done:     LDI    'D'
          OUT
          CALL   wait
          HLT
; copy: n words from main memory at src to the triangle list at dst. The list is on its own bus, so every word
; goes through the bridge (STL): the rasterizer must be idle.
copy:     LDA    n
          CMPI   0
          JEQ    copy_end
          LDA    src
          TAB
          LDX                            ; A = the word at src
          PSA
          LDA    dst
          TAB
          POA
          STL                            ; the list at dst = A
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
tris:     .DATA  320, 240, 1000, 0xFFFF  ; x, y, z, colour for each corner
          .DATA  520, 240, 1000, 0xF800
          .DATA  510, 302, 1000, 0xF800
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  505, 317, 1000, 0xFAE0
          .DATA  472, 370, 1000, 0xFAE0
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  461, 381, 1000, 0xFDE0
          .DATA  411, 418, 1000, 0xFDE0
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  397, 425, 1000, 0xDFE0
          .DATA  336, 439, 1000, 0xDFE0
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  320, 440, 1000, 0x7FE0
          .DATA  258, 430, 1000, 0x7FE0
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  243, 425, 1000, 0x1FE0
          .DATA  190, 392, 1000, 0x1FE0
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  179, 381, 1000, 0x07E7
          .DATA  142, 331, 1000, 0x07E7
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  135, 317, 1000, 0x07F3
          .DATA  121, 256, 1000, 0x07F3
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  120, 240, 1000, 0x07FF
          .DATA  130, 178, 1000, 0x07FF
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  135, 163, 1000, 0x04FF
          .DATA  168, 110, 1000, 0x04FF
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  179, 99, 1000, 0x01FF
          .DATA  229, 62, 1000, 0x01FF
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  243, 55, 1000, 0x181F
          .DATA  304, 41, 1000, 0x181F
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  320, 40, 1000, 0x781F
          .DATA  382, 50, 1000, 0x781F
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  397, 55, 1000, 0xD81F
          .DATA  450, 88, 1000, 0xD81F
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  461, 99, 1000, 0xF817
          .DATA  498, 149, 1000, 0xF817
          .DATA  320, 240, 1000, 0xFFFF
          .DATA  505, 163, 1000, 0xF80B
          .DATA  519, 224, 1000, 0xF80B
