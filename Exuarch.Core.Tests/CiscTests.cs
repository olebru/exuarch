using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class CiscTests
{
    private static readonly MachinePackage Cisc = BuiltInPackages.Get("CISC-16");

    private static (Machine Machine, long Ticks, int Deepest) Run(string source)
    {
        var c = new Machine(Cisc.Machine, source) { RecordHistory = false };
        long ticks = 0;
        int lowest = 0x10000;
        while (!c.IsHalted)
        {
            c.SingleStep();
            Assert.True(++ticks < 5_000_000, "the program did not halt");
            int sp = c.Device<Register>("sp").Data;
            if (sp != 0) lowest = Math.Min(lowest, sp);
        }
        Assert.Empty(c.MicrocodeWarnings);
        return (c, ticks, 0x10000 - lowest);
    }
    private static string Lcd(Machine c) => c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "").Trim();
    private static int[] Registers(Machine c) => c.Device<RegisterFile>("rf").Values.ToArray();

    [Fact]
    public void TheProgramsPrintTheOutputInTheReadme()
    {
        var sum = Run(Cisc.Program("Sum a table").Source);
        Assert.Equal("150", Lcd(sum.Machine));
        Assert.Equal(150, sum.Machine.Device<RamModule>("mem").ValueAt(sum.Machine.Assembler.labelLUT["total"]));
        Assert.Equal("Copied by MOV_PP", Lcd(Run(Cisc.Program("Copy a string").Source).Machine));

        var fib = Run(Cisc.Program("Recursive Fibonacci").Source);
        Assert.Equal("0 1 1 2 3 5 8 13 21 34 55", Lcd(fib.Machine));
        Assert.Equal(49831, fib.Ticks);
        const int CellsPerFrame = 4;
        Assert.Equal(10 * CellsPerFrame, fib.Deepest);
        Assert.Equal(0, fib.Machine.Device<Register>("sp").Data);
        foreach (var text in new[] { "`150`", "`Copied by MOV_PP`", "`0 1 1 2 3 5 8 13 21 34 55`", "49,831 ticks", "40 cells deep" }) Assert.Contains(text, Cisc.Readme);
    }

    [Fact]
    public void CodeIsDataRewritesItsOwnInstructions()
    {
        var source = Cisc.Program("Code is data").Source;
        var before = new Machine(Cisc.Machine, source).Device<RamModule>("mem");
        var run = Run(source);
        var c = run.Machine;
        var mem = c.Device<RamModule>("mem");
        var labels = c.Assembler.labelLUT;
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ", Lcd(c));
        Assert.Equal('A', before.ValueAt(labels["letter"] + 1));
        Assert.Equal('A' + 26, mem.ValueAt(labels["letter"] + 1));
        Assert.NotEqual(before.ValueAt(labels["stop"]), before.ValueAt(labels["start"]));
        Assert.Equal(mem.ValueAt(labels["stop"]), mem.ValueAt(labels["start"]));
        Assert.Equal(labels["start"], c.CurrentInstructionAddress);
        Assert.Equal(CodeIsDataTicks, run.Ticks);
        foreach (var text in new[] { "`ABCDEFGHIJKLMNOPQRSTUVWXYZ`", $"{CodeIsDataTicks:N0} ticks", "HARVARD-16" }) Assert.Contains(text, Cisc.Readme);
    }
    private const long CodeIsDataTicks = 838;

    [Theory]
    [InlineData("MOV_RI R1, 7\nMOV_RR R0, R1", 0, 7)]
    [InlineData("MOV_RA R0, cell\nHLT\ncell: .DATA 42", 0, 42)]
    [InlineData("LEA R1, cell\nMOV_RN R0, R1\nHLT\ncell: .DATA 42", 0, 42)]
    [InlineData("LEA R1, cell\nMOV_RX R0, R1, 2\nHLT\ncell: .DATA 1, 2, 42", 0, 42)]
    [InlineData("LEA R1, cell\nMOV_RX R0, R1, -1\nHLT\n.DATA 42\ncell: .DATA 1", 0, 42)]
    [InlineData("LEA R1, cell\nMOV_RP R0, R1\nMOV_RP R0, R1\nHLT\ncell: .DATA 1, 42", 0, 42)]
    [InlineData("LEA R1, cell\nMOV_XI R1, 1, 42\nMOV_RX R0, R1, 1\nHLT\ncell: .DATA 0, 0", 0, 42)]
    [InlineData("MOV_RI R0, 40\nLEA R1, cell\nADD_RN R0, R1\nHLT\ncell: .DATA 2", 0, 42)]
    [InlineData("LEA R1, cell\nADD_NI R1, 2\nMOV_RA R0, cell\nHLT\ncell: .DATA 40", 0, 42)]
    [InlineData("LEA R1, cell\nLEAX R2, R1, 3\nHLT\ncell: .DATA 0", 2, -1)]
    public void EveryModeFindsItsOperand(string program, int register, int expected)
    {
        var c = Run(program + "\nHLT").Machine;
        if (expected >= 0) Assert.Equal(expected, Registers(c)[register]);
        else Assert.Equal(c.Assembler.labelLUT["cell"] + 3, Registers(c)[register]);
    }

    [Fact]
    public void PostIncrementStepsThePointerAndIndexedAddressesLeaveTheFlagsAlone()
    {
        var c = Run("LEA R1, cell\nMOV_RP R0, R1\nCMP_RI R0, 5\nMOV_RX R2, R1, -1\nHLT\ncell: .DATA 5").Machine;
        Assert.Equal(c.Assembler.labelLUT["cell"] + 1, Registers(c)[1]);
        Assert.Equal(5, Registers(c)[2]);
        Assert.Equal(StatusRegister.ZeroFlag, c.Device<StatusRegister>("flags").Data & StatusRegister.ZeroFlag);
    }

    [Fact]
    public void EnterAndLeaveBuildAStackFrame()
    {
        var c = Run("MOV_RI R6, 99\nPUSH_I 7\nCALL f\nFREE 1\nHLT\nf: ENTER R6, 1\nMOV_RX R0, R6, 2\nMOV_XR R6, -1, R0\nMOV_RX R1, R6, 0\nLEAVE R6\nRET").Machine;
        Assert.Equal((7, 99, 99), (Registers(c)[0], Registers(c)[1], Registers(c)[6]));
        Assert.Equal(0, c.Device<Register>("sp").Data);
        var mem = c.Device<RamModule>("mem");
        const int FramePointer = 4093;
        var argument = mem.ValueAt(FramePointer + 2);
        var returnAddress = mem.ValueAt(FramePointer + 1);
        var callersFramePointer = mem.ValueAt(FramePointer);
        var local = mem.ValueAt(FramePointer - 1);
        Assert.Equal(new[] { 7, 7, 99, 7 }, new[] { argument, returnAddress, callersFramePointer, local });
    }

    [Theory]
    [InlineData("ADD_RR R0, R1", 10)]
    [InlineData("ADD_RX R0, R1, 2", 14)]
    [InlineData("ADD_XX R0, 1, R1, 2", 19)]
    [InlineData("MOV_PP R2, R1", 11)]
    public void InstructionsTakeTheTicksInTheReadme(string source, int expected)
    {
        var c = new Machine(Cisc.Machine, source + "\nHLT") { RecordHistory = false };
        int ticks = 0;
        do { c.SingleStep(); ticks++; } while (c.MicroStepRegister != 0);
        Assert.Equal(expected, ticks);
        Assert.Contains($"takes {expected}", Cisc.Readme.Replace("takes 10 ticks", "takes 10"));
    }

    [Fact]
    public void TheReadmeCountsTheInstructionsAndMicroSteps()
    {
        var microcode = Cisc.Machine.Decoder.Microcode;
        Assert.Equal(184, microcode.Instructions.Count);
        Assert.Equal(1692, new DecoderRom(microcode).OpCodesUsed);
        Assert.Equal(37, BuiltInPackages.Get("RISC-16").Machine.Decoder.Microcode.Instructions.Count);
        foreach (var text in new[] { "RISC-16 has 37 instructions; CISC-16 has 184, in 1,692 micro step addresses" }) Assert.Contains(text, Cisc.Readme);
        foreach (var op in new[] { "MOV", "ADD", "SUB", "AND", "CMP" }) Assert.Equal(30, microcode.Instructions.Count(i => i.Mnemonic.StartsWith(op + "_")));
    }
}
