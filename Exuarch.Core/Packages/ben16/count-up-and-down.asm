up:     OUT
        ADD    step
        JC     down
        JMP    up
down:   SUB    step
        OUT
        JZ     up
        JMP    down
step:   .DATA  256
