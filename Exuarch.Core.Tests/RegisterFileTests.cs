using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class RegisterFileTests
{
    private static readonly MachineDefinition Risc16 = BuiltInPackages.Get("RISC-16").Machine;
    private static AssemblyLanguage Language(int registers = 8) => new AssemblyLanguage(Risc16.Decoder.Microcode, 4096, registers);
    private static int Op(string mnemonic) => new DecoderRom(Risc16.Decoder.Microcode).FetchByteCodeFromMnemonic(mnemonic);

    private static Machine Run(string source, MachineDefinition machine = null)
    {
        var c = new Machine(machine ?? Risc16, source) { RecordHistory = false };
        while (!c.IsHalted) c.SingleStep();
        return c;
    }

    [Fact]
    public void SelectLatchesTheRegisterTheOtherLinesWorkOn()
    {
        var file = Run("MOVI R2, 77\nSUBI R3, R3, 1\nADDI R6, R6, 1\nMOV R0, R2\nHLT").Device<RegisterFile>("rf");
        Assert.Equal(new[] { 77, 0, 77, 0xFFFF, 0, 0, 1, 0 }, file.Values);
        Assert.Equal(0, file.Selected);
    }

    [Fact]
    public void TheCountComesFromTheFirstRegisterFile()
    {
        Assert.Equal(8, RegisterFile.CountIn(Risc16));
        Assert.Equal(0, RegisterFile.CountIn(BuiltInPackages.Get("BYOC-16").Machine));
        var machine = BuiltInPackages.Get("RISC-16").Machine;
        machine.FindDevice("rf").Parameters["count"] = System.Text.Json.JsonDocument.Parse("16").RootElement;
        Assert.Equal(16, RegisterFile.CountIn(machine));
        Assert.Equal(9, Run("MOVI R15, 9\nHLT", machine).Device<RegisterFile>("rf")[15]);
    }

    [Fact]
    public void RegisterOperandsAssembleToTheirNumber()
    {
        var result = Language().Analyze("MOV R7, r0\nOUT R3\nr1: B r1");
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { Op("MOV"), 7, 0, Op("OUT"), 3, Op("B"), 5 }, result.Cells);
    }

    [Theory]
    [InlineData("OUT R8", "OUT takes a register here: write R0 to R7, not 'R8'")]
    [InlineData("OUT loop\nloop: NOP", "OUT takes a register here: write R0 to R7, not 'loop'")]
    [InlineData("OUT 3", "OUT takes a register here: write R0 to R7, not '3'")]
    [InlineData("OUT RX", "OUT takes a register here: write R0 to R7, not 'RX'")]
    public void OnlyARegisterGoesInARegisterSlot(string source, string expected)
    {
        var result = Language().Analyze(source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message == expected);
    }

    [Fact]
    public void WithoutARegisterFileThereIsNothingToName()
    {
        var result = Language(0).Analyze("OUT R1");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("the machine has no register file"));
    }

    [Fact]
    public void TheEditorCompletesAndDescribesRegisters()
    {
        var language = Language(4);
        var items = language.Complete("OUT ", 1, 5);
        Assert.Equal(new[] { "R0", "R1", "R2", "R3" }, items.Select(i => i.Label));
        Assert.Contains("register **R2**", language.Hover("OUT R2", 1, 6));
        Assert.Contains("**OUT** register", language.Hover("OUT R2", 1, 2));
    }

    [Fact]
    public void ARegisterOperandNeedsARegisterFile()
    {
        var machine = BuiltInPackages.Get("RISC-16").Machine;
        machine.Devices.Remove(machine.FindDevice("rf"));
        var diagnostics = MicrocodeValidator.Validate(machine.Decoder.Microcode, machine);
        Assert.Contains(diagnostics, d => d.Instruction == "OUT" && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("no registerFile"));
    }
}
