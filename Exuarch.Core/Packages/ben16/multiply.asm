top:     LDA    x
         SUB    one
         JC     done
         STA    x
         LDA    product
         ADD    y
         STA    product
         JMP    top
done:    LDA    product
         OUT
         HLT
one:     .DATA  1
product: .DATA  0
x:       .DATA  123
y:       .DATA  45
