using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class PackageTests
{
    private static Machine Run(MachinePackage package, string source, int limit = 100000, string name = "program")
    {
        var c = new Machine(package.Machine, source) { RecordHistory = false };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < limit, $"{name} did not halt"); }
        Assert.Empty(c.MicrocodeWarnings);
        return c;
    }
    private static Machine Risc(string source, int limit = 100000)
    {
        return Run(BuiltInPackages.Get("RISC-16"), source, limit);
    }
    private static int[] Registers(Machine c) { return c.Device<RegisterFile>("rf").Values.ToArray(); }
    private static int Flags(Machine c) { return c.Device<StatusRegister>("flags").Data; }

    [Fact]
    public void EveryExampleHasAShortTaglineAndTopics()
    {
        foreach (var package in BuiltInPackages.Examples)
        {
            Assert.False(string.IsNullOrWhiteSpace(package.Tagline), $"{package.Name} has no tagline");
            Assert.True(package.Tagline.Length <= 50, $"{package.Name}'s tagline is {package.Tagline.Length} characters");
            Assert.False(package.Tagline.EndsWith('.'), $"{package.Name}'s tagline ends with a full stop");
            Assert.NotEmpty(package.Tags ?? new System.Collections.Generic.List<string>());
            Assert.Equal(package.Tagline, package.Summary);
        }
    }

    [Fact]
    public void APackageWithoutATaglineIsSummedUpByItsFirstSentence()
    {
        var package = new MachinePackage { Description = "Two registers and a loop. Nothing else." };
        Assert.Equal("Two registers and a loop", package.Summary);
        Assert.Equal("", new MachinePackage().Summary);
        Assert.True(package.Matches("REGISTERS loop"));
        Assert.False(package.Matches("registers stack"));
        Assert.True(package.Matches("  "));
    }

    [Fact]
    public void TaglinesAndTagsTravelWithAnExportedPackage()
    {
        var json = BuiltInPackages.Get("ROM-16").ToJson();
        var again = MachinePackage.FromJson(json);

        Assert.Equal("A ROM, and memory that walks by itself", again.Tagline);
        Assert.Equal(new[] { "memory", "rom", "graphics" }, again.Tags);
        Assert.DoesNotContain("\"tagline\"", new MachinePackage { Name = "x", Machine = new MachineDefinition() }.ToJson());
    }

    [Fact]
    public void BuiltInPackagesLoadWithTheDefaultFirst()
    {
        Assert.Equal(new[] { "TINY-16", "BLAZE-16", "BYOC-16", "CISC-16", "COPRO-16", "DSP-16", "FIELD-16", "FLIP-16", "GPU-16", "HARVARD-16", "IRQ-16", "MOVE-16", "RISC-16", "ROM-16", "STACK-16", "TURBO-16", "WORM-16" }, BuiltInPackages.Examples.Select(p => p.Name));
        // The tutorial starts come after the examples, in tutorial order.
        Assert.Equal(TutorialMachines.Starts.Select(s => s.PackageName), BuiltInPackages.All.Skip(17).Select(p => p.Name));
        Assert.Same(BuiltInPackages.All[0], BuiltInPackages.Default);
        Assert.Equal("TINY-16", BuiltInPackages.Default.Name);
        foreach (var package in BuiltInPackages.Examples)
        {
            Assert.False(string.IsNullOrWhiteSpace(package.Description), package.Name);
            Assert.NotEmpty(package.Programs);
            Assert.Empty(MicrocodeValidator.Validate(package.Machine.Decoder.Microcode, package.Machine));
        }
    }

    [Fact]
    public void EveryBuiltInPackageHasALevel()
    {
        var byLevel = BuiltInPackages.Levels.ToDictionary(l => l, l => BuiltInPackages.All.Where(p => BuiltInPackages.Level(p.Name) == l).Select(p => p.Name).ToList());
        Assert.Equal(BuiltInPackages.All.Count, byLevel.Values.Sum(names => names.Count));
        Assert.Equal(new[] { "TINY-16", "BYOC-16", "STACK-16" }, byLevel["simple"]);
        Assert.Equal(new[] { "CISC-16", "COPRO-16", "FIELD-16", "HARVARD-16", "IRQ-16", "MOVE-16", "RISC-16", "ROM-16" }, byLevel["advanced"]);
        Assert.Equal(new[] { "BLAZE-16", "DSP-16", "FLIP-16", "GPU-16", "TURBO-16", "WORM-16" }, byLevel["ludicrous"]);
        Assert.Equal(Enumerable.Range(2, 9).Select(n => TutorialMachines.Starts.Single(s => s.Number == n).PackageName), byLevel["tutorial"]);
        Assert.Null(BuiltInPackages.Level("My machine"));
        Assert.Null(BuiltInPackages.Level(null));
    }

    // These loop for ever on purpose: the long running demo and the interactive programs.
    private static readonly string[] LoopingPrograms = { "Sketch with the arrow keys", "Paddle game", "Three things at once", "Falling blocks", "A spinning cube", "A steady spinning cube", "A spinning wireframe cube", "Colour cycling, 80 x 60" };
    // These halt, but take far longer than a test can wait; DspTests checks what they draw.
    private static readonly string[] SlowPrograms = { "Mandelbrot, 80 x 60", "Mandelbrot, 160 x 120", "Mandelbrot, 640 x 480", "Lightning at the top of the set" };

    [Fact]
    public void EveryProgramInEveryExampleAssemblesWithoutWarningsAndHalts()
    {
        foreach (var package in BuiltInPackages.Examples)
        {
            var language = new AssemblyLanguage(package.Machine.Decoder.Microcode, 4096, RegisterFile.CountIn(package.Machine));
            foreach (var program in package.Programs)
            {
                var result = language.Analyze(program.Source);
                Assert.True(result.Success && result.Diagnostics.Count == 0, $"{package.Name} / {program.Name}: {string.Join(" | ", result.Diagnostics)}");
                Assert.Equal(program.Source, language.FormatDocument(program.Source));
                if (LoopingPrograms.Contains(program.Name) || SlowPrograms.Contains(program.Name))
                {
                    var looping = new Machine(package.Machine, program.Source) { RecordHistory = false };
                    for (int i = 0; i < 10000; i++) looping.SingleStep();
                    Assert.False(looping.IsHalted, $"{package.Name} / {program.Name} stopped within 10000 ticks");
                    Assert.Empty(looping.MicrocodeWarnings);
                    continue;
                }
                Run(package, program.Source, 5_000_000, $"{package.Name} / {program.Name}");
            }
        }
    }

    [Fact]
    public void GetReturnsACopy()
    {
        var copy = BuiltInPackages.Get("RISC-16");
        copy.Programs.Clear();
        copy.Machine.Devices.Clear();
        Assert.NotEmpty(BuiltInPackages.Get("RISC-16").Programs);
        Assert.NotEmpty(BuiltInPackages.All.Single(p => p.Name == "RISC-16").Machine.Devices);
        Assert.Throws<ArgumentException>(() => BuiltInPackages.Get("nope"));
    }

    [Fact]
    public void FallingBlocksNeedsTheKeypadAndAQuarterMegahertz()
    {
        var needs = BuiltInPackages.Get("IRQ-16").Program("Falling blocks").Needs;
        Assert.True(needs.Keypad);
        Assert.Equal(250_000, needs.MinHz);
        Assert.Null(BuiltInPackages.Get("IRQ-16").Program("Three things at once").Needs);
    }

    [Fact]
    public void AProgramThatNeedsTheKeypadIsOnAMachineWithOne()
    {
        foreach (var package in BuiltInPackages.All)
        {
            foreach (var program in package.Programs.Where(p => p.Needs?.Keypad == true))
                Assert.True(new Machine(package.Machine, program.Source).Devices.OfType<Keypad>().Any(), $"{package.Name}: {program.Name} needs a keypad");
        }
    }

    [Fact]
    public void ProgramNeedsRoundTripInPackageJson()
    {
        var package = BuiltInPackages.Get("IRQ-16");
        var back = MachinePackage.FromJson(package.ToJson()).Program("Falling blocks").Needs;
        Assert.True(back.Keypad);
        Assert.Equal(250_000, back.MinHz);
    }

    [Fact]
    public void PackageJsonRoundTrips()
    {
        var package = BuiltInPackages.Default;
        var json = package.ToJson();
        var back = MachinePackage.FromJson(json);
        Assert.Equal(package.Name, back.Name);
        Assert.Equal(package.Programs.Select(p => (p.Name, p.Description, p.Source)), back.Programs.Select(p => (p.Name, p.Description, p.Source)));
        Assert.Equal(package.Machine.ToJson(), back.Machine.ToJson());
        Assert.Equal(json, back.ToJson());
    }

    [Theory]
    [InlineData("{", "not valid JSON")]
    [InlineData("{\"name\": \"x\"}", "needs a \"machine\"")]
    public void BadPackagesAreReported(string json, string expected)
    {
        var e = Assert.Throws<MachineDefinitionException>(() => MachinePackage.FromJson(json));
        Assert.Contains(expected, e.Message);
    }

    // PEA and PEB peek at the stack in bank 0, like PSA and POA use it, whatever bank the program has selected,
    // and leave that bank selected and SP where it was.
    [Fact]
    public void ByocPeeksReadTheStackInBankZero()
    {
        var byoc = BuiltInPackages.Get("BYOC-16");
        var c = Run(byoc, "LAI 7\nPSA\nSWB 1\nLAI 0\nPEA\nSTA 0\nPEB\nHLT");
        var mmu = c.Device<MMU>("mmu");
        Assert.Equal(1, mmu.SelectedBankNumber);
        Assert.Equal(7, mmu.RamBanks[1].ValueAt(0));
        Assert.Equal(7, c.Device<Register>("regb").Data);
        Assert.Equal(0xFFFF, c.Device<Register>("regsp").Data);
    }

    // LPA and LPB follow the pointer stored at the operand address; LRA reads the operand address itself.
    [Fact]
    public void ByocPointerLoadsFollowThePointer()
    {
        var c = Run(BuiltInPackages.Get("BYOC-16"), @"
            LPA    ptr
            LPB    ptr
            HLT
    ptr:    .DATA  target
    target: .DATA  42");
        Assert.Equal(42, c.Device<Register>("rega").Data);
        Assert.Equal(42, c.Device<Register>("regb").Data);
        Assert.Equal(0, c.Device<Register>("regs").Data);
        c = Run(BuiltInPackages.Get("BYOC-16"), "LRA ptr\nHLT\nptr: .DATA 7");
        Assert.Equal(7, c.Device<Register>("rega").Data);
    }

    [Fact]
    public void ExampleDataHasByocsPrograms()
    {
        Assert.Equal(BuiltInPackages.Get("BYOC-16").Programs.Select(p => p.Name), ExampleData.Programs.Select(p => p.Name));
    }

    [Fact]
    public void RiscRegistersAreOperandsOfARegisterFile()
    {
        var machine = BuiltInPackages.Get("RISC-16").Machine;
        Assert.Equal("registerFile", machine.FindDevice("rf").Type);
        Assert.Equal(8, RegisterFile.CountIn(machine));
        // Three register operations: the destination and two sources.
        var add = machine.Decoder.Microcode.FindInstruction("ADD");
        Assert.Equal(new[] { OperandType.Register, OperandType.Register, OperandType.Register }, add.OperandTypes);
        // Only loads and stores reach memory: no other instruction puts a register on the bus as an address.
        var addressing = machine.Decoder.Microcode.Instructions.Where(i => i.Steps.Any(s => s.Signals.Contains("rf.output") && s.Signals.Contains("mem.loadmar"))).Select(i => i.Mnemonic);
        Assert.Equal(new[] { "LDR", "STR" }, addressing);
    }

    [Fact]
    public void RiscHelloCallsASubroutineAndPrints()
    {
        var c = Risc(BuiltInPackages.Get("RISC-16").Program("Hello, world on the LCD").Source);
        Assert.Equal("Hello from RISC-16!", c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd());
    }

    [Fact]
    public void RiscFibonacciPrintsTheSequenceInDecimal()
    {
        var c = Risc(BuiltInPackages.Get("RISC-16").Program("Fibonacci on the LCD").Source, 1_000_000);
        var text = c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "");
        Assert.Equal("1 1 2 3 5 8 13 21 34 55 89 144 233 377 610 987 1597 2584 4181 6765 10946 17711 28657 46368", text.Trim());
    }

    [Fact]
    public void RiscGradientFillsTheScreen()
    {
        var c = Risc(BuiltInPackages.Get("RISC-16").Program("Colour gradient on the screen").Source, 20_000_000);
        var fb = c.Device<Framebuffer>("fb");
        Assert.Equal(Framebuffer.Width * Framebuffer.Height, fb.WriteCount);
        for (int y = 0; y < Framebuffer.Height; y += 7)
        {
            for (int x = 0; x < Framebuffer.Width; x += 13)
            {
                int expected = (x / 20) << 11 | (y / 8) << 5 | 0x10;
                Assert.True(expected == fb.Pixels[y * Framebuffer.Width + x], $"pixel {x},{y}: expected 0x{expected:X4}, found 0x{fb.Pixels[y * Framebuffer.Width + x]:X4}");
            }
        }
    }

    [Fact]
    public void RiscInstructionsTakeAnyRegisters()
    {
        var c = Risc(@"
            MOVI  R7, 40
            MOVI  R3, 2
            ADD   R5, R7, R3
            MOV   R0, R5
            ADR   R4, data
            STR   R0, R4
            ADDI  R4, R4, 1
            LDR   R6, R4
            SUBI  R6, R6, 1
            HLT
    data:   .DATA 0, 10");
        var data = c.Assembler.labelLUT["data"];
        Assert.Equal(new[] { 42, 0, 0, 2, data + 1, 42, 9, 40 }, Registers(c));
        Assert.Equal(42, c.Device<RamModule>("mem").ValueAt(data));
    }

    [Fact]
    public void RiscThreeRegisterOperationsLeaveTheirSourcesAlone()
    {
        foreach (var (op, expected) in new[] { ("ADD", 0x102C), ("SUB", 0xE4C), ("AND", 0x30), ("ORR", 0xFFC), ("EOR", 0xFCC) })
        {
            var c = Risc($@"
                MOVI R1, 0x0F3C
                MOVI R2, 0x00F0
                {op} R3, R1, R2
                HLT");
            Assert.True(expected == Registers(c)[3], $"{op}: 0x{Registers(c)[3]:X}");
            Assert.Equal((0x0F3C, 0xF0), (Registers(c)[1], Registers(c)[2]));
        }
        var shifts = Risc(@"
            MOVI R1, 0x8001
            MOVI R2, 1
            LSL  R3, R1, R2
            LSRI R4, R1, 1
            LSLI R5, R1, 4
            HLT");
        Assert.Equal((0x0002, 0x4000, 0x0010), (Registers(shifts)[3], Registers(shifts)[4], Registers(shifts)[5]));
        Assert.Equal(0, Flags(shifts) & StatusRegister.CarryFlag);
        Assert.NotEqual(0, Flags(Risc("MOVI R1, 0x8001\nLSLI R0, R1, 1\nHLT")) & StatusRegister.CarryFlag);
    }

    [Theory]
    [InlineData("BEQ", 5, 5, true)]
    [InlineData("BEQ", 5, 6, false)]
    [InlineData("BNE", 5, 6, true)]
    [InlineData("BNE", 5, 5, false)]
    [InlineData("BCS", 3, 5, true)]
    [InlineData("BCS", 5, 3, false)]
    [InlineData("BCC", 5, 3, true)]
    [InlineData("BCC", 3, 5, false)]
    [InlineData("BMI", 3, 5, true)]
    [InlineData("BMI", 5, 5, false)]
    [InlineData("BPL", 5, 5, true)]
    [InlineData("BPL", 3, 5, false)]
    public void RiscBranchesFollowTheFlags(string branch, int a, int b, bool taken)
    {
        foreach (var compare in new[] { $"CMP R4, R5", $"CMPI R4, {b}" })
        {
            var c = Risc($@"
                MOVI  R4, {a}
                MOVI  R5, {b}
                {compare}
                {branch} yes
                MOVI  R2, 1
                HLT
        yes:    MOVI  R2, 2
                HLT");
            Assert.Equal(taken ? 2 : 1, Registers(c)[2]);
            Assert.Equal((a, b), (Registers(c)[4], Registers(c)[5]));
        }
    }

    // The tick counts the README gives, fetch included.
    [Theory]
    [InlineData("ADD R0, R1, R2", 12, "12 ticks with fetch")]
    [InlineData("ADDI R0, R1, 5", 11, "`ADDI Rd, Rs, value` takes 11")]
    [InlineData("MOV R0, R1", 9, "`MOV` and `LDR` take 9")]
    [InlineData("LDR R0, R1", 9, "`MOV` and `LDR` take 9")]
    [InlineData("MOVI R0, 3", 6, "`MOVI` takes 6")]
    [InlineData("BL R7, next\nnext: NOP", 8, "A call costs 8 ticks")]
    public void RiscInstructionsTakeTheTicksTheReadmeSays(string source, int expected, string readme)
    {
        var c = new Machine(BuiltInPackages.Get("RISC-16").Machine, source + "\nHLT") { RecordHistory = false };
        int ticks = 0;
        do { c.SingleStep(); ticks++; } while (c.MicroStepRegister != 0);
        Assert.Equal(expected, ticks);
        Assert.Contains(readme, BuiltInPackages.Get("RISC-16").Readme);
    }

    [Fact]
    public void RiscCallsKeepTheReturnAddressInARegister()
    {
        // BL R7 links and BX R7 returns; a routine that calls another saves R7 on the stack R6 points at, as the README shows.
        var c = Risc(@"
            BL    R7, outer
            MOVI  R2, 2
            HLT
    outer:  SUBI  R6, R6, 1
            STR   R7, R6
            BL    R7, inner
            LDR   R7, R6
            ADDI  R6, R6, 1
            BX    R7
    inner:  MOVI  R1, 1
            BX    R7");
        Assert.Equal((1, 2, 0), (Registers(c)[1], Registers(c)[2], Registers(c)[6]));
        // The saved link is the address after BL R7, outer, which is three cells long. It went to the top of memory:
        // 0 - 1 wraps to 65535, and the address to 4095.
        Assert.Equal(3, c.Device<RamModule>("mem").ValueAt(4095));
        var readme = BuiltInPackages.Get("RISC-16").Readme;
        Assert.Contains("        SUBI   R6, R6, 1     ; make room\n        STR    R7, R6        ; save the link\n        BL     R7, inner\n        LDR    R7, R6        ; get it back\n        ADDI   R6, R6, 1\n        BX     R7", readme);
    }

    private static Machine Tiny(string source)
    {
        return Run(BuiltInPackages.Get("TINY-16"), source);
    }

    [Theory]
    [InlineData("Say Hi", "Hi")]
    [InlineData("2 + 3", "2+3=5")]
    [InlineData("Count down from 9 to 0", "9876543210")]
    public void TinyProgramsPrint(string name, string expected)
    {
        var c = Tiny(BuiltInPackages.Get("TINY-16").Program(name).Source);
        Assert.Equal(expected, c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "").Trim());
    }

    // The README lists the opcodes, each the ROM address of the instruction's first step, and the cells Say Hi
    // assembles to.
    [Fact]
    public void TinyOpcodesAreWhereEachInstructionsStepsStart()
    {
        var instructions = BuiltInPackages.Get("TINY-16").Machine.Decoder.Microcode.Instructions;
        Assert.Equal(new[] { "LOAD", "ADD", "SUB", "OUT", "JUMP", "JZ", "HALT" }, instructions.Select(i => i.Mnemonic));
        var c = new Machine(BuiltInPackages.Get("TINY-16").Machine, BuiltInPackages.Get("TINY-16").Program("Say Hi").Source);
        Assert.Equal(new[] { 2, 72, 10, 2, 105, 10, 15 }, Enumerable.Range(0, 7).Select(c.Device<RamModule>("mem").ValueAt));
    }

    [Fact]
    public void TinyLoadTakesFourTicksAndOutThree()
    {
        var c = new Machine(BuiltInPackages.Get("TINY-16").Machine, "LOAD 'H'\nOUT\nHALT") { RecordHistory = false };
        for (int i = 0; i < 4; i++) c.SingleStep();
        Assert.Equal('H', c.Device<Register>("a").Data);
        Assert.Equal(2, c.Device<Register>("pc").Data);
        for (int i = 0; i < 3; i++) c.SingleStep();
        Assert.Equal("H", c.Device<CharacterDisplay>("lcd").Text.Trim());
        Assert.Equal(3, c.Device<Register>("pc").Data);
    }

    [Fact]
    public void TinySubSetsZeroOnlyWhenTheAnswerIsZero()
    {
        var c = Tiny(@"
            LOAD 5
            SUB  5
            JZ   yes
            LOAD 1
            HALT
    yes:    LOAD 2
            ADD  1
            JZ   yes
            HALT");
        Assert.Equal(3, c.Device<Register>("a").Data);
    }
}
