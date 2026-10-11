using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class WormTests
{
    private static readonly MachinePackage Worm = BuiltInPackages.Get("WORM-16");

    private static (Machine Machine, long Ticks) Run(string source, long limit = 2_000_000)
    {
        var c = new Machine(Worm.Machine, source) { RecordHistory = false };
        long ticks = 0;
        while (!c.IsHalted)
        {
            c.SingleStep();
            Assert.True(++ticks < limit, "the program did not halt");
        }
        Assert.Empty(c.MicrocodeWarnings);
        return (c, ticks);
    }
    private static string Lcd(Machine c) => c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "").TrimEnd();
    private static int Head(Machine c) => c.Device<Register>("head").Data;
    private static int Tail(Machine c) => c.Device<Register>("tail").Data;

    [Theory]
    [InlineData("Count down and go", "9876543210GO", 712)]
    [InlineData("Cell division", "* ** **** ********", 407)]
    [InlineData("Fibonacci", "0 1 1 2 3 5 8 13 21 34 55 89", 1151)]
    [InlineData("Two phases", "54321 EDCBA", 815)]
    public void TheProgramsPrintTheOutputInTheReadme(string name, string expected, long ticks)
    {
        var run = Run(Worm.Program(name).Source);
        Assert.Equal(expected, Lcd(run.Machine));
        Assert.Equal(ticks, run.Ticks);
        Assert.Contains($"`{expected}`", Worm.Readme);
    }

    [Fact]
    public void TheReadmeListsTheCountdownsTicks()
    {
        Assert.Contains("`9876543210GO` in 712 ticks", Worm.Readme);
    }

    [Fact]
    public void NoInstructionJumps()
    {
        var microcode = Worm.Machine.Decoder.Microcode;
        foreach (var instruction in microcode.AllInstructions)
        {
            foreach (var step in instruction.Steps) Assert.DoesNotContain("head.load", step.Signals);
        }
    }

    [Theory]
    [InlineData("OUTC_D 'x'", 0)]
    [InlineData("OUTC 'x'", 2)]
    [InlineData("OUTC_2 'x'", 4)]
    public void EachFateWritesItsCopiesToTheTail(string instruction, int cellsWritten)
    {
        var run = Run($"TAIL_D end\n{instruction}\nHLT\nend:");
        var tailStart = run.Machine.Assembler.labelLUT["end"];
        Assert.Equal(tailStart + cellsWritten, Tail(run.Machine));
        var mem = run.Machine.Device<RamModule>("mem");
        for (int i = 0; i < cellsWritten; i += 2)
        {
            const int InstructionCellAfterTailD = 2;
            Assert.Equal(mem.ValueAt(InstructionCellAfterTailD), mem.ValueAt(tailStart + i));
            Assert.Equal('x', mem.ValueAt(tailStart + i + 1));
        }
    }

    [Fact]
    public void LivingWhileNIsClearMeansSkippedOnceItIsSet()
    {
        var open = Run("TAIL_D end\nLDA_D 1\nOUTC_P 'a'\nHLT\nend:");
        Assert.Equal("a", Lcd(open.Machine));
        Assert.Equal(open.Machine.Assembler.labelLUT["end"] + 2, Tail(open.Machine));
        var closed = Run("TAIL_D end\nLDA_D -1\nOUTC_P 'a'\nHLT\nend:");
        Assert.Equal("", Lcd(closed.Machine));
        Assert.Equal(closed.Machine.Assembler.labelLUT["end"], Tail(closed.Machine));
    }

    [Theory]
    [InlineData(5, 9, true)]
    [InlineData(9, 9, true)]
    [InlineData(10, 9, false)]
    public void UptoIsOpenWhileAIsAtMostTheValue(int a, int v, bool open)
    {
        var c = new Machine(Worm.Machine, $"TAIL_D end\nLDA_D {a}\nUPTO {v}\nHLT\nend:") { RecordHistory = false };
        while (!c.IsHalted) c.SingleStep();
        var end = c.Assembler.labelLUT["end"];
        Assert.Equal(open ? end + 2 : end, Tail(c));
        Assert.Equal(open ? 0 : StatusRegister.NegativeFlag, c.Status & StatusRegister.NegativeFlag);
        Assert.Equal(a, c.Device<Register>("a").Data);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(9, "9")]
    [InlineData(10, "10")]
    [InlineData(42, "42")]
    [InlineData(99, "99")]
    public void Out2PrintsUpToTwoDigits(int a, string expected)
    {
        var c = new Machine(Worm.Machine, $"TAIL_D end\nLDA_D {a}\nOUT2 10, '0'\nHLT\nend:") { RecordHistory = false };
        int flags = -1;
        while (!c.IsHalted)
        {
            if (c.CurrentInstructionAddress == 4 && flags < 0) flags = c.Status;
            c.SingleStep();
        }
        Assert.Equal(expected, Lcd(c));
        Assert.Equal(a, c.Device<Register>("a").Data);
        Assert.Equal(flags, c.Status);
    }

    [Fact]
    public void HatchCarriesItsEggUntilNIsSet()
    {
        var source = "TAIL_D end\nLDA_D 7\nHATCH 3\nOUTC 'e'\nHLT\nGATE 99\nend:";
        const int HatchCell = 4;
        const int HatchAndEggCells = 5;
        const int GateCell = 9;
        var c = new Machine(Worm.Machine, source) { RecordHistory = false };
        while (c.CurrentInstructionAddress != GateCell) c.SingleStep();
        Assert.Equal("", Lcd(c));
        Assert.Equal(7, c.Device<Register>("a").Data);
        Assert.Equal(0, c.Status & StatusRegister.NegativeFlag);
        var mem = c.Device<RamModule>("mem");
        int end = c.Assembler.labelLUT["end"];
        for (int i = 0; i < HatchAndEggCells; i++) Assert.Equal(mem.ValueAt(HatchCell + i), mem.ValueAt(end + i));
        Assert.Equal("e", Lcd(Run(source).Machine));
    }

    [Fact]
    public void TheWormGoesRoundTheRing()
    {
        var run = Run("TAIL_D end\nLDA_D 1500\nGATE 1\nSUB_P 1\nHATCH 3\nOUTC '!'\nHLT\nend:");
        Assert.Equal("!", Lcd(run.Machine));
        Assert.True(Head(run.Machine) > 3 * 4096, $"the head only got to {Head(run.Machine)}");
    }

    [Fact]
    public void AWormThatOnlyGrowsCatchesItsOwnHead()
    {
        var c = new Machine(Worm.Machine, "TAIL_D end\nOUTC_2 '*'\nend:") { RecordHistory = false };
        long ticks = 0;
        while (Tail(c) - Head(c) < 4096 && !c.IsHalted)
        {
            c.SingleStep();
            Assert.True(++ticks < 2_000_000, "the worm never filled the ring");
        }
        Assert.True(Tail(c) - Head(c) >= 4096);
    }

    [Fact]
    public void HatchsMicrocodeLoopIsNotAFetch()
    {
        var c = new Machine(Worm.Machine, "TAIL_D end\nLDA_D 1\nHATCH 5\nOUTC 'a'\nOUTC 'b'\nHLT\nend:") { RecordHistory = false };
        const int HatchCell = 4;
        while (c.CurrentInstructionAddress != HatchCell) c.SingleStep();
        while (c.CurrentInstructionAddress == HatchCell) c.SingleStep();
        Assert.Equal(c.Assembler.labelLUT["end"], c.CurrentInstructionAddress);
        Assert.Equal("HATCH", c.InstructionAt(c.CurrentInstructionAddress.Value).Mnemonic);
    }

    [Fact]
    public void TheInstructionAtACopyDecodesFromMemory()
    {
        var c = new Machine(Worm.Machine, Worm.Program("Count down and go").Source) { RecordHistory = false };
        var end = c.Assembler.labelLUT["end"];
        while (c.CurrentInstructionAddress != end) c.SingleStep();
        var line = c.InstructionAt(end);
        Assert.Equal("GATE", line.Mnemonic);
        Assert.Equal(new[] { "0030" }, line.Operands);
        Assert.Null(c.Assembler.Listing.FirstOrDefault(l => l.IsInstruction && l.Address == end));
        Assert.Equal("'9'", c.InstructionAt(2).Operands[0]);
    }
}
