start:  LDI    1
        STA    x
        LDI    0
loop:   OUT
        ADD    x
        JC     last
        STA    y
        LDA    x
        STA    z
        LDA    y
        STA    x
        LDA    z
        JMP    loop
last:   LDA    x
        OUT
        JMP    start
x:      .DATA  0
y:      .DATA  0
z:      .DATA  0
