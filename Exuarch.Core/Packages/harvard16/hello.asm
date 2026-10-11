; Hello, world across three buses
; The string is in program memory on ibus. LPM brings each character over the opr bridge to A on dbus,
; and OUT sends it over the io bridge to the LCD on iobus. Watch the Run view: while OUT drives iobus, the next
; opcode is already being fetched on ibus.
        CLT
        LBA      msg         ; B = the address of the string in program memory
loop:   LPM                  ; A = program memory[B]
        CMPI     0
        JEQ      done        ; a 0 ends the string
        OUT
        INB                  ; move to the next character
        JMP      loop
done:   HLT

msg:    .STRING  "Hello from three buses!"

