using System;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class AssemblerTests
{
    private readonly DecoderRom rom = new DecoderRom(ExampleData.ROMDATA);

    private int Op(string mnemonic) => rom.FetchByteCodeFromMnemonic(mnemonic);

    [Fact]
    public void AssemblesExampleProgram()
    {
        var bytes = new Assembler(rom).Assemble(ExampleData.SRC);
        Assert.Equal(new int[]
        {
            Op("LAI"), 15, Op("PSA"), Op("PSA"),
            Op("LRA"), 13, Op("LRB"), 14,
            Op("PSA"), Op("ADD"), Op("PSA"), Op("JMP"), 9,
            65, 1
        }, bytes);
    }

    [Fact]
    public void AssemblingTwiceReturnsTheSameResult()
    {
        var assembler = new Assembler(rom);
        var first = assembler.Assemble(ExampleData.SRC);
        var second = assembler.Assemble(ExampleData.SRC);
        Assert.Equal(first, second);
    }

    [Fact]
    public void LineEndingsBlankLinesAndTrailingTabsAreTolerated()
    {
        var src = "start:\tLAI\t#1\t\r\n\r\n\tJMP\tstart\n";
        var bytes = new Assembler(rom).Assemble(src);
        Assert.Equal(new int[] { Op("LAI"), 1, Op("JMP"), 0 }, bytes);
    }

    [Fact]
    public void LabelOnItsOwnLinePointsAtNextInstruction()
    {
        var src = "\tNOP\nloop:\n\tJMP\tloop";
        var bytes = new Assembler(rom).Assemble(src);
        Assert.Equal(new int[] { Op("NOP"), Op("JMP"), 1 }, bytes);
    }

    [Fact]
    public void DataDirectiveAcceptsMultipleOperands()
    {
        var bytes = new Assembler(rom).Assemble("data:\t.DATA\t#1,#2,data");
        Assert.Equal(new int[] { 1, 2, 0 }, bytes);
    }

    [Theory]
    [InlineData("\tFOO", "unknown mnemonic 'FOO'")]
    [InlineData("\tJMP\tnowhere", "unknown label 'nowhere'")]
    [InlineData("\tLAI\t#65536", "'#65536' is not a number")]
    [InlineData("\t.DWORD\t#1", "unknown directive '.DWORD'")]
    [InlineData("a:\tNOP\na:\tNOP", "defined more than once")]
    [InlineData("\tLAI\t#1\tcomment", "missing ',' before 'comment'")]
    [InlineData("label\tNOP", "unknown mnemonic 'label'")]
    public void ErrorsReportLineAndCause(string src, string expected)
    {
        var e = Assert.Throws<FormatException>(() => new Assembler(rom).Assemble(src));
        Assert.Contains("Line ", e.Message);
        Assert.Contains(expected, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramLargerThanMemoryIsRejected()
    {
        var src = string.Join("\n", System.Linq.Enumerable.Repeat("\tNOP", 257));
        Assert.Throws<FormatException>(() => new Assembler(rom, 256).Assemble(src));
    }
}
