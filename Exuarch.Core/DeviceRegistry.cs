using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    public delegate IBusDevice DeviceFactory(DeviceBuildContext context);

    public class DeviceTypeInfo
    {
        public string Type { get; set; }
        public string Category { get; set; } = "Other";
        public string Description { get; set; } = "";
        public List<string> Ports { get; set; } = new List<string> { DeviceBuildContext.DefaultPort };
        public List<ConnectionInfo> Connections { get; set; } = new List<ConnectionInfo>();
        public List<string> OptionalPorts { get; set; } = new List<string>();
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        public List<ControlLineInfo> ControlLines { get; set; } = new List<ControlLineInfo>();
        public bool TakesContents { get; set; }
    }
    public class ControlLineInfo
    {
        public string Name { get; set; }
        public string Description { get; set; } = "";
        public string Drives { get; set; }
        public string Reads { get; set; }
        public string WritesConnection { get; set; }
        public bool Halts { get; set; }

        public static ControlLineInfo Output(string name, string description, string port = DeviceBuildContext.DefaultPort)
            => new ControlLineInfo { Name = name, Description = description, Drives = port };
        public static ControlLineInfo Input(string name, string description, string port = DeviceBuildContext.DefaultPort)
            => new ControlLineInfo { Name = name, Description = description, Reads = port };
        public static ControlLineInfo Internal(string name, string description)
            => new ControlLineInfo { Name = name, Description = description };
    }
    public class ConnectionInfo
    {
        public string Name { get; set; }
        public string Description { get; set; } = "";
        public string OnPort { get; set; }
    }
    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Description { get; set; } = "";
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 65535;
        public int Default { get; set; } = 0;
    }

    public class DeviceRegistry
    {
        private readonly Dictionary<string, DeviceFactory> factories = new Dictionary<string, DeviceFactory>();
        private readonly Dictionary<string, DeviceTypeInfo> infos = new Dictionary<string, DeviceTypeInfo>();

        public static DeviceRegistry CreateDefault(TimeProvider time = null)
        {
            var size = new ParameterInfo { Name = "size", Description = "Number of 16 bit cells", Min = 1, Max = 65536, Default = MemoryModule.DefaultSize };
            List<ControlLineInfo> RegisterLines() => new List<ControlLineInfo>
            {
                ControlLineInfo.Output("output", "Put the value on the bus"),
                ControlLineInfo.Input("load", "Take the value from the bus"),
                ControlLineInfo.Internal("reset", "Set to 0"),
                ControlLineInfo.Internal("inc", "Add 1; 65535 wraps to 0"),
                ControlLineInfo.Internal("dec", "Subtract 1; 0 wraps to 65535"),
            };
            List<ControlLineInfo> MemoryLines() => new List<ControlLineInfo>
            {
                ControlLineInfo.Input("loadmar", "Take the address from the bus"),
                ControlLineInfo.Output("outputmar", "Put the address on the bus"),
                ControlLineInfo.Output("output", "Put the cell at the address on the bus"),
                ControlLineInfo.Internal("incmar", "Add 1 to the address after this tick's read or write, wrapping at the end of memory"),
                ControlLineInfo.Internal("decmar", "Subtract 1 from the address after this tick's read or write, wrapping at the start of memory"),
            };
            List<ControlLineInfo> RamLines()
            {
                var lines = MemoryLines();
                lines.Add(ControlLineInfo.Input("load", "Store the bus value at the address"));
                return lines;
            }

            var registry = new DeviceRegistry();
            registry.Register("register", c => new Register(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo { Category = "Registers", Description = "Holds one 16 bit value between ticks. output puts it on the bus and load stores the bus value; reset, inc and dec change it in place, wrapping from 65535 to 0 and back. It starts at 0", ControlLines = RegisterLines() });
            registry.Register("registerFile", c => new RegisterFile(c.Name, c.Id, c.Bus(), c.IntParameter("count", RegisterFile.DefaultCount, 2, 16)),
                new DeviceTypeInfo
                {
                    Category = "Registers",
                    Description = "A bank of numbered registers behind one select input, as most real CPUs have. select takes a register number from the bus, and output, load, reset, inc and dec then act on that register. Instructions with register operands name them R0, R1 and so on, and the assembler turns the name into the number",
                    Parameters = { new ParameterInfo { Name = "count", Description = "Number of registers, R0 up to R(count - 1)", Min = 2, Max = 16, Default = RegisterFile.DefaultCount } },
                    ControlLines =
                    {
                        ControlLineInfo.Input("select", "Select the register whose number is on the bus, from the end of the tick"),
                        ControlLineInfo.Output("output", "Put the selected register on the bus"),
                        ControlLineInfo.Input("load", "Take the value on the bus into the selected register"),
                        ControlLineInfo.Internal("reset", "Set the selected register to 0"),
                        ControlLineInfo.Internal("inc", "Add 1 to the selected register; 65535 wraps to 0"),
                        ControlLineInfo.Internal("dec", "Subtract 1 from the selected register; 0 wraps to 65535"),
                    }
                });
            ParameterInfo FieldParameter(string name, string description, int defaultField) =>
                new ParameterInfo { Name = name, Description = description, Min = 0, Max = InstructionFormat.MaxFields - 1, Default = defaultField };
            int FieldOf(DeviceBuildContext c, string name, int defaultField) => c.IntParameter(name, defaultField, 0, InstructionFormat.MaxFields - 1);
            registry.Register("multiportRegisterFile", c => new MultiportRegisterFile(c.Name, c.Id,
                    c.Bus(MultiportRegisterFile.PortA), c.Bus(MultiportRegisterFile.PortB), c.Bus(MultiportRegisterFile.WritePort),
                    c.Connection<InstructionWord>("word"),
                    c.IntParameter("count", RegisterFile.DefaultCount, 2, 16),
                    FieldOf(c, "readA", MultiportRegisterFile.DefaultReadAField),
                    FieldOf(c, "readB", MultiportRegisterFile.DefaultReadBField),
                    FieldOf(c, "write", MultiportRegisterFile.DefaultWriteField),
                    c.OptionalBus(MultiportRegisterFile.SecondWritePort),
                    FieldOf(c, "write2", MultiportRegisterFile.DefaultSecondWriteField)),
                new DeviceTypeInfo
                {
                    Category = "Registers",
                    Description = "A register file with two read ports, a write port and an optional second write port. The register numbers come straight from opcode fields of the connected instruction word, wired to the address inputs, so no step selects a register: outputa and outputb put two registers on buses a and b in the same tick, and loadw stores bus w in a third. With port v connected, loadv stores bus v in the register named by the write2 field in the same tick, so one step can swap two registers or load a value and move a pointer on. Both writes to one register in one tick stop the machine with an error. Instructions with register operands name them R0, R1 and so on",
                    Ports = new List<string> { MultiportRegisterFile.PortA, MultiportRegisterFile.PortB, MultiportRegisterFile.WritePort, MultiportRegisterFile.SecondWritePort },
                    OptionalPorts = { MultiportRegisterFile.SecondWritePort },
                    Connections = { new ConnectionInfo { Name = "word", Description = "The instruction word whose fields give the register numbers" } },
                    Parameters =
                    {
                        new ParameterInfo { Name = "count", Description = "Number of registers in the file, R0 up to R(count - 1). An instruction reaches them through its register fields, so a field of 3 bits reaches 8 registers and a field of 4 bits reaches 16", Min = 2, Max = 16, Default = RegisterFile.DefaultCount },
                        FieldParameter("readA", "Which of the instruction word's four opcode fields, 0 to 3, holds the register number for read port a. The number is the field's value, so the field's width sets how many registers it can name", MultiportRegisterFile.DefaultReadAField),
                        FieldParameter("readB", "Which of the four opcode fields, 0 to 3, holds the register number for read port b", MultiportRegisterFile.DefaultReadBField),
                        FieldParameter("write", "Which of the four opcode fields, 0 to 3, holds the register number for the write port", MultiportRegisterFile.DefaultWriteField),
                        FieldParameter("write2", "Which of the four opcode fields, 0 to 3, holds the register number for the second write port, port v; it can be the field a read port uses", MultiportRegisterFile.DefaultSecondWriteField),
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Output("outputa", "Put the register named by the readA field on bus a", MultiportRegisterFile.PortA),
                        ControlLineInfo.Output("outputb", "Put the register named by the readB field on bus b", MultiportRegisterFile.PortB),
                        ControlLineInfo.Input("loadw", "Take the value on bus w into the register named by the write field", MultiportRegisterFile.WritePort),
                        ControlLineInfo.Input("loadv", "Take the value on bus v into the register named by the write2 field; needs port v connected", MultiportRegisterFile.SecondWritePort),
                    }
                });
            registry.Register("statusRegister",c => new StatusRegister(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo { Category = "Registers", Description = "A 16 bit register for the flags. An ALU connected to it writes its low four bits, Z zero, C carry, V overflow and N negative, and the decoder reads those bits to pick which steps of an instruction run. Microcode can load, save or change it like any register", ControlLines = RegisterLines() });
            registry.Register("dualPortRegister", c => new DualPortRegister(c.Name, c.Id, c.Bus("a"), c.Bus("b")),
                new DeviceTypeInfo
                {
                    Category = "Registers",
                    Description = "A register that sits on two buses: load it from one and output it on the other to move a value between them",
                    Ports = new List<string> { "a", "b" },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loada", "Take the value from bus a", "a"),
                        ControlLineInfo.Input("loadb", "Take the value from bus b", "b"),
                        ControlLineInfo.Output("outputa", "Put the value on bus a", "a"),
                        ControlLineInfo.Output("outputb", "Put the value on bus b", "b"),
                        ControlLineInfo.Internal("reset", "Set to 0"),
                        ControlLineInfo.Internal("inc", "Add 1; 65535 wraps to 0"),
                        ControlLineInfo.Internal("dec", "Subtract 1; 0 wraps to 65535"),
                    }
                });
            registry.Register("instructionRegister", c => new InstructionRegister(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "Control",
                    Description = "The decoder's micro step counter. It counts up every tick on its own and, with the status flags, addresses the decoder ROM step that runs next. load jumps to the steps of the opcode on the bus, reset clears it to 0 where fetch starts",
                    ControlLines =
                    {
                        ControlLineInfo.Input("load", "Jump to the step address on the bus: the start of an opcode's steps"),
                        ControlLineInfo.Internal("reset", "Clear to 0, the start of the fetch routine"),
                    }
                });
            var wordLines = new List<ControlLineInfo>
            {
                ControlLineInfo.Input("load", "Latch the instruction word on the bus, at the same time as the micro step register loads it"),
                ControlLineInfo.Output("output", "Put the whole instruction word on the bus"),
            };
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                wordLines.Add(ControlLineInfo.Output(InstructionWord.FieldLine(field), $"Put field {field} of the word on the fields bus, in the low bits with 0 above them", InstructionWord.FieldPort));
            }
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                wordLines.Add(ControlLineInfo.Output(InstructionWord.SignedFieldLine(field), $"Put field {field} on the fields bus sign extended: its top bit copied into every bit above it, so a negative field stays negative", InstructionWord.FieldPort));
            }
            registry.Register("instructionWord", c => new InstructionWord(c.Name, c.Id, c.Bus(), c.Bus(InstructionWord.FieldPort)),
                new DeviceTypeInfo
                {
                    Category = "Control",
                    Description = "A latch for the instruction word, for a decoder that reads opcode fields (microcode opcodeBits). It loads the word from its data bus, where the program memory is. The word's top bits are the opcode; the operand fields below them, such as register numbers and small numbers, are wired through buffers of their own to its fields bus, one line per field: the same bus as data on a one bus machine, or the data path's bus when the program has a bus of its own. The microcode's fields list where each field sits",
                    Ports = new List<string> { DeviceBuildContext.DefaultPort, InstructionWord.FieldPort },
                    ControlLines = wordLines,
                });
            registry.Register("clock", c => new Clock(c.Name, c.Id),
                new DeviceTypeInfo
                {
                    Category = "Control",
                    Description = "Counts the ticks. Its disable line stops it, which halts the machine when this is the clock the machine names as its halt clock",
                    Ports = new List<string>(),
                    ControlLines = { new ControlLineInfo { Name = "disable", Description = "Halt the machine", Halts = true } }
                });
            var aluLines = new List<ControlLineInfo>
            {
                ControlLineInfo.Output("add", "Put a + b on the bus; C on a carry out, V on signed overflow"),
                ControlLineInfo.Output("sub", "Put a - b on the bus; C when a < b unsigned (a borrow), V on signed overflow"),
                ControlLineInfo.Internal("cmp", "Set the flags for a - b, like sub, without driving the bus"),
                ControlLineInfo.Output("and", "Put a AND b on the bus; C and V are cleared"),
                ControlLineInfo.Output("orr", "Put a OR b on the bus; C and V are cleared"),
                ControlLineInfo.Output("eor", "Put a XOR b on the bus; C and V are cleared"),
                ControlLineInfo.Output("lsl", "Put a shifted left by b (0-15) on the bus; C is the last bit out, V is cleared"),
                ControlLineInfo.Output("lsr", "Put a shifted right by b (0-15) on the bus; C is the last bit out, V is cleared"),
            };
            aluLines.ForEach(line => line.WritesConnection = "status");
            registry.Register("alu", c => new ALU(c.Name, c.Id, c.Connection<Register>("a"), c.Connection<Register>("b"), c.Connection<Register>("status"), c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "Arithmetic",
                    Description = "Arithmetic and logic on the registers connected as a and b. add, sub, and, orr, eor, lsl and lsr put the result on the bus, cmp only compares. Every operation replaces all four flags in the connected status register: Z when the result is 0, N from its top bit, and C and V as listed for each operation",
                    Connections =
                    {
                        new ConnectionInfo { Name = "a", Description = "First operand register" },
                        new ConnectionInfo { Name = "b", Description = "Second operand register" },
                        new ConnectionInfo { Name = "status", Description = "Register that receives the flags" },
                    },
                    ControlLines = aluLines
                });
            registry.Register("ram", c => new RamModule(c.Name, c.Id, c.Bus(), c.IntParameter("size", MemoryModule.DefaultSize, 1, 65536)),
                new DeviceTypeInfo { Category = "Memory", Description = "Read/write memory with its own address register (MAR): loadmar takes an address from the bus, output reads that cell and load writes the bus value into it", Parameters = { size }, ControlLines = RamLines() });
            registry.Register("rom", c => new RomModule(c.Name, c.Id, c.Bus(), c.IntParameter("size", MemoryModule.DefaultSize, 1, 65536), c.Definition.Contents),
                new DeviceTypeInfo
                {
                    Category = "Memory",
                    Description = "Read-only memory with its own address register (MAR), filled before the machine starts with the contents you give it: tables, text and constants written as .DATA, .STRING and .BASE64 lines, labels and all. loadmar takes an address from the bus and output reads that cell; nothing can write it",
                    Parameters = { size },
                    ControlLines = MemoryLines(),
                    TakesContents = true,
                });
            var mmuLines = RamLines();
            mmuLines.Add(ControlLineInfo.Input("loadcs", "Select the bank given on the bus"));
            mmuLines.Add(ControlLineInfo.Output("outputcs", "Put the selected bank number on the bus"));
            mmuLines.Add(ControlLineInfo.Internal("select0stack", "Select bank 0, the stack bank"));
            registry.Register("mmu", c =>
                {
                    int banks = c.IntParameter("banks", MMU.DefaultBanks, 1, 256), bankSize = c.IntParameter("bankSize", MemoryModule.DefaultSize, 1, 65536);
                    return new MMU(c.Name, c.Id, c.Bus(), banks, bankSize);
                },
                new DeviceTypeInfo
                {
                    Category = "Memory",
                    Description = "Banks of RAM behind a chip select register: loadcs picks a bank from the bus and the memory lines then work on that bank. select0stack picks bank 0, the stack bank",
                    Parameters =
                    {
                        new ParameterInfo { Name = "banks", Description = "Number of banks", Min = 1, Max = 256, Default = MMU.DefaultBanks },
                        new ParameterInfo { Name = "bankSize", Description = "16 bit cells per bank", Min = 1, Max = 65536, Default = MemoryModule.DefaultSize },
                    },
                    ControlLines = mmuLines
                });
            registry.Register("display", c => new CharacterDisplay(c.Name, c.Id, c.Bus(), c.IntParameter("columns", 16, 1, 64), c.IntParameter("rows", 4, 1, 16)),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A character LCD: load prints the low 8 bits of the bus at the cursor as a Latin-1 character and moves on, clear blanks it. Line feed and carriage return move the cursor",
                    Parameters =
                    {
                        new ParameterInfo { Name = "columns", Description = "Characters per row", Min = 1, Max = 64, Default = 16 },
                        new ParameterInfo { Name = "rows", Description = "Rows of characters", Min = 1, Max = 16, Default = 4 },
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Input("load", "Print the character on the bus at the cursor"),
                        ControlLineInfo.Internal("clear", "Blank the display and move the cursor home"),
                    }
                });
            registry.Register("segmentDisplay", c => new SegmentDisplay(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "Six seven segment digits that show the value on the bus: load shows it in decimal, 0 to 65535, loadsigned as a two's complement number, -32768 to 32767, and loadhex as four hex digits, 0000 to FFFF. It stands in for a row of seven segment displays and the decoder that lights their segments",
                    ControlLines =
                    {
                        ControlLineInfo.Input("load", "Show the value on the bus in decimal"),
                        ControlLineInfo.Input("loadhex", "Show the value on the bus as four hex digits"),
                        ControlLineInfo.Input("loadsigned", "Show the value on the bus as a two's complement number"),
                    }
                });
            registry.Register("framebuffer", c => new Framebuffer(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A 640 x 480 colour screen, one RGB565 word per pixel. loadx and loady move the cursor, plot writes the bus value there and moves right, skip moves right without writing, plotdown and skipdown do the same moving down, clear blanks the screen",
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadx", "Set the cursor column from the bus (0-639)"),
                        ControlLineInfo.Input("loady", "Set the cursor row from the bus (0-479)"),
                        ControlLineInfo.Input("plot", "Write the RGB565 colour on the bus at the cursor, then move right"),
                        ControlLineInfo.Internal("skip", "Move the cursor right without writing"),
                        ControlLineInfo.Input("plotdown", "Write the RGB565 colour on the bus at the cursor, then move down one row, wrapping from the bottom row to the top"),
                        ControlLineInfo.Internal("skipdown", "Move the cursor down one row without writing"),
                        ControlLineInfo.Internal("clear", "Blank the screen and move the cursor home"),
                    }
                });
            registry.Register("doubleFramebuffer", c => new DoubleFramebuffer(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A 640 x 480 colour screen with two buffers: the front one is shown while loadx, loady, plot, skip and clear work on the back one, and swap exchanges them, so a whole frame appears at once. Anything that can draw on a framebuffer, such as a blitter or a rasterizer, can draw on it",
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadx", "Set the cursor column from the bus (0-639)"),
                        ControlLineInfo.Input("loady", "Set the cursor row from the bus (0-479)"),
                        ControlLineInfo.Input("plot", "Write the RGB565 colour on the bus at the cursor in the back buffer, then move right"),
                        ControlLineInfo.Internal("skip", "Move the cursor right without writing"),
                        ControlLineInfo.Input("plotdown", "Write the RGB565 colour on the bus at the cursor, then move down one row, wrapping from the bottom row to the top"),
                        ControlLineInfo.Internal("skipdown", "Move the cursor down one row without writing"),
                        ControlLineInfo.Internal("clear", "Blank the back buffer and move the cursor home; the screen is unchanged"),
                        ControlLineInfo.Internal("swap", "Show the back buffer and draw on the old front one from now on; the cursor stays"),
                    }
                });
            registry.Register("rasterizer", c => new Rasterizer(c.Name, c.Id, c.Bus("host"), c.Bus("list"), c.Bus("video"),
                    c.Connection<Framebuffer>("screen"), c.OptionalConnection<DepthBuffer>("depth"), c.Connection<MemoryModule>("memory")),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A triangle rasterizer, the heart of a fixed function GPU. Load it with the address and number of triangles in its memory and start it: it reads each triangle over its list bus (12 words: x, y, z and an RGB565 colour per corner), then fills it on its video bus, one transfer per tick, blending the corner colours (Gouraud shading). With a depth buffer connected it draws a pixel only where the triangle is nearer. status reads 1 while it runs, and it raises an interrupt request when done",
                    Ports = new List<string> { "host", "list", "video" },
                    Connections =
                    {
                        new ConnectionInfo { Name = "screen", Description = "The framebuffer it draws on, on its video bus", OnPort = "video" },
                        new ConnectionInfo { Name = "depth", Description = "Optional: the depth buffer it tests and updates, on its video bus", OnPort = "video" },
                        new ConnectionInfo { Name = "memory", Description = "The memory it reads triangles from, on its list bus", OnPort = "list" },
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadaddr", "Take the address of the first triangle from the host bus", "host"),
                        ControlLineInfo.Input("loadcount", "Take the number of triangles from the host bus", "host"),
                        ControlLineInfo.Internal("start", "Start drawing the triangles; it then runs by itself"),
                        ControlLineInfo.Output("status", "Put 1 on the host bus while running, 0 when idle", "host"),
                    }
                });
            registry.Register("texturedRasterizer", c => new TexturedRasterizer(c.Name, c.Id, c.Bus("host"), c.Bus("list"), c.Bus("tex"), c.Bus("video"),
                    c.Connection<Framebuffer>("screen"), c.OptionalConnection<DepthBuffer>("depth"), c.Connection<MemoryModule>("memory"), c.Connection<MemoryModule>("texture"),
                    c.IntParameter("size", TexturedRasterizer.DefaultSize, 2, TexturedRasterizer.MaxSize)),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A triangle rasterizer that maps a texture onto each triangle. It reads each triangle over its list bus, 19 words: x, y, depth, texture u, texture v and an RGB565 shade for each corner, then the address of a square texture in its texture memory. For each pixel it works out u and v, perspective correct when all three depths are above 0, reads that texel over its tex bus and multiplies it by the blended shade, so a white shade shows the texture as it is. With a depth buffer connected it draws a pixel only where the triangle is nearer, and reads no texel for a hidden one. status reads 1 while it runs, and it raises an interrupt request when done",
                    Ports = new List<string> { "host", "list", "tex", "video" },
                    Parameters = { new ParameterInfo { Name = "size", Description = "Width and height of a texture in texels, a power of two; u and v wrap round it", Min = 2, Max = TexturedRasterizer.MaxSize, Default = TexturedRasterizer.DefaultSize } },
                    Connections =
                    {
                        new ConnectionInfo { Name = "screen", Description = "The framebuffer it draws on, on its video bus", OnPort = "video" },
                        new ConnectionInfo { Name = "depth", Description = "Optional: the depth buffer it tests and updates, on its video bus", OnPort = "video" },
                        new ConnectionInfo { Name = "memory", Description = "The memory it reads triangles from, on its list bus", OnPort = "list" },
                        new ConnectionInfo { Name = "texture", Description = "The memory it reads texels from, on its tex bus", OnPort = "tex" },
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadaddr", "Take the address of the first triangle from the host bus", "host"),
                        ControlLineInfo.Input("loadcount", "Take the number of triangles from the host bus", "host"),
                        ControlLineInfo.Internal("start", "Start drawing the triangles; it then runs by itself"),
                        ControlLineInfo.Output("status", "Put 1 on the host bus while running, 0 when idle", "host"),
                    }
                });
            registry.Register("depthBuffer", c => new DepthBuffer(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A 640 x 480 memory of depths, one per framebuffer pixel, smaller being nearer. It has a cursor like the framebuffer: loadx and loady set it, output reads the depth there, load writes one and next moves right. clear sets every depth to 65535, the farthest",
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadx", "Set the cursor column from the bus (0-639)"),
                        ControlLineInfo.Input("loady", "Set the cursor row from the bus (0-479)"),
                        ControlLineInfo.Output("output", "Put the depth at the cursor on the bus"),
                        ControlLineInfo.Input("load", "Write the depth on the bus at the cursor"),
                        ControlLineInfo.Internal("next", "Move the cursor right, after any load in the same tick"),
                        ControlLineInfo.Internal("clear", "Set every depth to 65535 and move the cursor home"),
                    }
                });
            registry.Register("mac", c => new MultiplyAccumulate(c.Name, c.Id, c.Bus(), c.IntParameter("shift", MultiplyAccumulate.DefaultFractionBits, 0, 15)),
                new DeviceTypeInfo
                {
                    Category = "Arithmetic",
                    Description = "A multiply-accumulate unit for fixed point maths. It takes two signed operands, a and b, and keeps a 32 bit accumulator: mul sets it to a times b, mac adds a times b, div divides it by b. output puts the accumulator shifted right by shift on the bus, so with shift 8 the numbers are 8.8 fixed point and 256 is 1.0",
                    Parameters = { new ParameterInfo { Name = "shift", Description = "Fraction bits: output is the accumulator shifted right by this much", Min = 0, Max = 15, Default = 8 } },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loada", "Take the signed operand a from the bus"),
                        ControlLineInfo.Input("loadb", "Take the signed operand b from the bus"),
                        ControlLineInfo.Internal("mul", "Accumulator = a × b"),
                        ControlLineInfo.Internal("mac", "Accumulator = accumulator + a × b"),
                        ControlLineInfo.Internal("div", "Accumulator = (accumulator << shift) / b, keeping the fixed point scale"),
                        ControlLineInfo.Output("output", "Put accumulator >> shift on the bus, clamped to 16 bits"),
                    }
                });
            registry.Register("blitter", c => new Blitter(c.Name, c.Id, c.Bus("host"), c.Bus("video"), c.Connection<Framebuffer>("screen")),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A graphics coprocessor. Load a rectangle and a colour into it from the host bus and start it: it then fills the rectangle on its video bus, one transfer per tick, driving the connected framebuffer while the CPU runs its own program. status reads 1 while it runs, a start while it runs is ignored, and it raises an interrupt request when a rectangle is done",
                    Ports = new List<string> { "host", "video" },
                    Connections = { new ConnectionInfo { Name = "screen", Description = "The framebuffer it draws on, on its video bus", OnPort = "video" } },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadx", "Take the rectangle's left column from the host bus", "host"),
                        ControlLineInfo.Input("loady", "Take the rectangle's top row from the host bus", "host"),
                        ControlLineInfo.Input("loadw", "Take the rectangle's width from the host bus", "host"),
                        ControlLineInfo.Input("loadh", "Take the rectangle's height from the host bus", "host"),
                        ControlLineInfo.Input("loadcolour", "Take the RGB565 fill colour from the host bus", "host"),
                        ControlLineInfo.Internal("start", "Start filling the rectangle; it then runs by itself"),
                        ControlLineInfo.Output("status", "Put 1 on the host bus while running, 0 when idle", "host"),
                    }
                });
            registry.Register("columnBlitter", c => new ColumnBlitter(c.Name, c.Id, c.Bus("host"), c.Bus("tex"), c.Bus("video"),
                    c.Connection<Framebuffer>("screen"), c.Connection<MemoryModule>("memory"), c.IntParameter("queue", ColumnBlitter.DefaultQueueSize, 1, ColumnBlitter.MaxQueueSize)),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "A graphics coprocessor that draws texture columns, stretched or shrunk to any height. Each job is a span: a column, a top row, a width, a height, the texture column's address in its memory, a start and a step in 8.8 fixed point. A job of width w draws the same texture column w times, side by side. Pixel p shows the texel at address + ((start + p × step) >> 8), read over the tex bus and written down the screen with plotdown on the video bus. A texel is read once however many pixels show it, and the next cell streams with incmar, so a column costs about one tick per pixel. Jobs wait in a queue while the CPU runs its own program, and a swap job in the queue shows a double buffered frame when the columns before it are done; texels equal to the key are skipped, so parts of a texture can be see-through. status reads the number of queued jobs, and it raises an interrupt request when the queue is empty",
                    Ports = new List<string> { "host", "tex", "video" },
                    Parameters = { new ParameterInfo { Name = "queue", Description = "Number of jobs the queue holds; push while it is full is ignored", Min = 1, Max = ColumnBlitter.MaxQueueSize, Default = ColumnBlitter.DefaultQueueSize } },
                    Connections =
                    {
                        new ConnectionInfo { Name = "screen", Description = "The framebuffer it draws on, on its video bus", OnPort = "video" },
                        new ConnectionInfo { Name = "memory", Description = "The memory it reads texels from, on its tex bus", OnPort = "tex" },
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadx", "Take the column from the host bus", "host"),
                        ControlLineInfo.Input("loady", "Take the top row from the host bus", "host"),
                        ControlLineInfo.Input("loadw", "Take the width in columns from the host bus; it starts at 1", "host"),
                        ControlLineInfo.Input("loadh", "Take the height in pixels from the host bus", "host"),
                        ControlLineInfo.Input("loadsrc", "Take the address of the texture column's first texel from the host bus", "host"),
                        ControlLineInfo.Input("loadstart", "Take the texel position of the top pixel, 8.8 fixed point, from the host bus", "host"),
                        ControlLineInfo.Input("loadstep", "Take the texel step from one pixel to the next, 8.8 fixed point, from the host bus; 256 is one texel per pixel", "host"),
                        ControlLineInfo.Input("loadkey", "Take the key from the host bus: texels equal to it are skipped and leave the screen as it was", "host"),
                        ControlLineInfo.Internal("nokey", "Clear the key, so every texel is drawn"),
                        ControlLineInfo.Internal("push", "Add a job with the loaded values to the queue; ignored when the width or height is 0 or the queue is full. The values stay loaded for the next job"),
                        ControlLineInfo.Internal("swap", "Add a job to the queue that turns on the screen's swap line when it is reached, so a frame is shown once every column before it is drawn; it takes one tick, and does nothing on a framebuffer without swap"),
                        ControlLineInfo.Output("status", "Put the number of queued jobs, the one being drawn included, on the host bus", "host"),
                    }
                });
            registry.Register("interruptController", c => new InterruptController(c.Name, c.Id, c.Bus(),
                    Enumerable.Range(0, InterruptController.Sources).Select(i => c.InterruptSource($"irq{i}")).ToList()),
                new DeviceTypeInfo
                {
                    Category = "Control",
                    Description = "Collects interrupt requests from up to four devices (irq0 to irq3) into pending bits. Name it as decoder.interrupts and microcode can test the I condition: 1 when interrupts are enabled and an unmasked request is pending",
                    Connections =
                    {
                        new ConnectionInfo { Name = "irq0", Description = "Interrupt source for pending bit 0 (a timer, real time clock, keypad, keyboard, blitter or rasterizer)" },
                        new ConnectionInfo { Name = "irq1", Description = "Interrupt source for pending bit 1" },
                        new ConnectionInfo { Name = "irq2", Description = "Interrupt source for pending bit 2" },
                        new ConnectionInfo { Name = "irq3", Description = "Interrupt source for pending bit 3" },
                    },
                    ControlLines =
                    {
                        ControlLineInfo.Internal("enable", "Allow interrupts"),
                        ControlLineInfo.Internal("disable", "Hold interrupts off"),
                        ControlLineInfo.Input("loadmask", "Take the mask from the bus: bit n set lets irq n interrupt"),
                        ControlLineInfo.Output("output", "Put the pending, unmasked request bits on the bus"),
                        ControlLineInfo.Input("ack", "Clear the pending bits that are set in the bus value"),
                    }
                });
            registry.Register("timer", c => new TickTimer(c.Name, c.Id, c.Bus(), c.IntParameter("period", 1000, 0, 65535)),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "Counts clock ticks and raises an interrupt request every period ticks while it runs, a steady beat that does not depend on the program. Connect it to an interrupt controller",
                    Parameters = { new ParameterInfo { Name = "period", Description = "Ticks between interrupt requests", Min = 0, Max = 65535, Default = 1000 } },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadperiod", "Take the period, in ticks, from the bus"),
                        ControlLineInfo.Internal("start", "Start counting from 0"),
                        ControlLineInfo.Internal("stop", "Stop counting"),
                        ControlLineInfo.Output("output", "Put the current count on the bus"),
                    }
                });
            registry.Register("rtc", c => new RealTimeClock(c.Name, c.Id, c.Bus(), c.IntParameter("interval", 1000, 0, 65535), time),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "Raises an interrupt request every interval milliseconds of real time while it runs, however fast or slowly the machine ticks: a real time clock. Where a timer counts ticks, this one keeps to the wall clock. The first tick after an interval has passed raises one request, even when the machine was paused for several. Connect it to an interrupt controller",
                    Parameters = { new ParameterInfo { Name = "interval", Description = "Milliseconds between interrupt requests", Min = 0, Max = 65535, Default = 1000 } },
                    ControlLines =
                    {
                        ControlLineInfo.Input("loadinterval", "Take the interval, in milliseconds, from the bus"),
                        ControlLineInfo.Internal("start", "Start timing an interval from now"),
                        ControlLineInfo.Internal("stop", "Stop timing"),
                        ControlLineInfo.Output("output", "Put the interval, in milliseconds, on the bus"),
                    }
                });
            registry.Register("keypad", c => new Keypad(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = "The arrow keys and space, read like a register: output puts one bit per key on the bus (1 up, 2 down, 4 left, 8 right, 16 space). A quick tap is kept until the CPU reads it, and every key pressed raises an interrupt request",
                    ControlLines =
                    {
                        ControlLineInfo.Output("output", "Put the key bits on the bus: 1 up, 2 down, 4 left, 8 right, 16 space"),
                    }
                });
            registry.Register("keyboard", c => new Keyboard(c.Name, c.Id, c.Bus()),
                new DeviceTypeInfo
                {
                    Category = "I/O",
                    Description = $"A full keyboard that sends IBM PC/XT scan codes: one code when a key goes down, again while it repeats, and the same code plus 0x80 when it comes up. Codes wait in a queue of {Keyboard.Capacity}, oldest first, and every code added raises an interrupt request. Translate codes to characters with a layout table in a ROM",
                    ControlLines =
                    {
                        ControlLineInfo.Output("output", "Put the oldest scan code on the bus and take it out of the queue, or 0 when the queue is empty"),
                    }
                });
            return registry;
        }

        public void Register(string type, DeviceFactory factory, DeviceTypeInfo info = null)
        {
            info ??= new DeviceTypeInfo();
            info.Type = type;
            factories[type] = factory;
            infos[type] = info;
        }
        public bool IsRegistered(string type)
        {
            return type != null && factories.ContainsKey(type);
        }
        public IEnumerable<string> Types { get { return factories.Keys; } }
        public IEnumerable<DeviceTypeInfo> TypeInfos { get { return infos.Values; } }
        public DeviceTypeInfo Info(string type)
        {
            return type != null && infos.TryGetValue(type, out var info) ? info : null;
        }
        public DeviceTypeInfo InfoFor(DeviceDefinition device)
        {
            return Info(device.Type) ?? new DeviceTypeInfo { Type = device.Type, Ports = device.Ports().Select(p => p.Key).ToList() };
        }
        internal DeviceFactory Factory(string type)
        {
            return factories[type];
        }
    }

    public class DeviceBuildContext
    {
        public const string DefaultPort = "data";
        private readonly DeviceDefinition definition;
        private readonly IReadOnlyDictionary<string, Bus> buses;
        private readonly Func<string, IBusDevice> resolveDevice;

        internal DeviceBuildContext(DeviceDefinition definition, IReadOnlyDictionary<string, Bus> buses, Func<string, IBusDevice> resolveDevice)
        {
            this.definition = definition;
            this.buses = buses;
            this.resolveDevice = resolveDevice;
        }
        public DeviceDefinition Definition { get { return definition; } }
        public string Id { get { return definition.Id; } }
        public string Name { get { return definition.Name ?? definition.Id; } }

        public Bus OptionalBus(string port)
        {
            var busId = definition.Ports().Where(p => p.Key == port).Select(p => p.Value).FirstOrDefault();
            return busId == null ? null : buses[busId];
        }

        public Bus Bus(string port = DefaultPort)
        {
            var busId = definition.Ports().Where(p => p.Key == port).Select(p => p.Value).FirstOrDefault();
            if (busId == null)
            {
                throw Error(port == DefaultPort
                    ? "needs a bus connection, set \"bus\""
                    : $"needs a bus connection for port '{port}', set \"buses\": {{ \"{port}\": \"<bus id>\" }}");
            }
            return buses[busId];
        }
        public T Connection<T>(string name) where T : class, IBusDevice
        {
            if (!definition.Connections.TryGetValue(name, out var targetId))
            {
                throw Error($"needs connection '{name}'");
            }
            var target = resolveDevice(targetId);
            return target as T ?? throw Error($"connection '{name}' must be a {typeof(T).Name}, but '{targetId}' is a {target.GetType().Name}");
        }
        public T OptionalConnection<T>(string name) where T : class, IBusDevice
        {
            return definition.Connections.ContainsKey(name) ? Connection<T>(name) : null;
        }
        public IInterruptSource InterruptSource(string name)
        {
            if (!definition.Connections.TryGetValue(name, out var targetId)) return null;
            var target = resolveDevice(targetId);
            return target as IInterruptSource ?? throw Error($"connection '{name}' must be a device that raises interrupts (a timer, real time clock, keypad, keyboard, blitter or rasterizer), but '{targetId}' is a {target.GetType().Name}");
        }
        public int IntParameter(string name, int defaultValue, int min, int max)
        {
            if (!definition.Parameters.TryGetValue(name, out var value)) return defaultValue;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < min || result > max)
            {
                throw Error($"parameter '{name}' must be a whole number between {min} and {max}");
            }
            return result;
        }
        public MachineDefinitionException Error(string message)
        {
            return new MachineDefinitionException($"Device '{definition.Id}' ({definition.Type}): {message}.");
        }
    }
}
