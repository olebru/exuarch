using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class OpcodeFieldTests
{
    private static MachinePackage Field16() => BuiltInPackages.Get("FIELD-16");
    private static MachinePackage Risc16() => BuiltInPackages.Get("RISC-16");

    private static Machine Build(MachinePackage package, string source) => new Machine(package.Machine, source);

    private static Machine Run(MachinePackage package, string source, int limit = 3_000_000)
    {
        var machine = Build(package, source);
        foreach (var _ in machine.Run().Take(limit)) { }
        return machine;
    }

    private static string Lcd(Machine machine) => System.Text.Encoding.Latin1.GetString(machine.Device<CharacterDisplay>("lcd").Cells).Replace("\0", " ").Trim();

    [Fact]
    public void ThreeRegistersPackIntoOneWordBelowTheOpcode()
    {
        var machine = Build(Field16(), "ADD R5, R1, R2\nHLT");

        Assert.Equal(0b000110_101_001_010_0, machine.ProgramByteCode[0]);
        Assert.Equal(2, machine.ProgramByteCode.Length);
    }

    [Fact]
    public void OperandsOutsideTheFieldsFollowInCellsOfTheirOwn()
    {
        var machine = Build(Field16(), "MOVI R3, 1000\nHLT");

        Assert.Equal(new[] { (3 << 10) | (3 << 7), 1000, 2 << 10 }, machine.ProgramByteCode);
    }

    [Fact]
    public void AValueThatDoesNotFitItsFieldIsAnError()
    {
        var result = new AssemblyLanguage(Field16().Machine.Decoder.Microcode, registerCount: 8).Analyze("LSLI R0, R1, 16\nLSLI R0, R1, 15\nLSLI R0, R1, -8");

        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.Line);
        Assert.Contains("'16' does not fit in the 4 bit field at bits 3-0", error.Message);
    }

    [Fact]
    public void FieldsFitUnsignedOrAsANegativeOfTheirWidth()
    {
        var slot = new FieldSlot(0, 4);
        Assert.True(slot.Fits(15));
        Assert.False(slot.Fits(16));
        Assert.True(slot.Fits(0xFFF8));
        Assert.False(slot.Fits(0xFFF7));
        Assert.Equal(0xFFFF, slot.ReadSigned(0x000F));
        Assert.Equal(7, slot.ReadSigned(0x0007));
    }

    [Fact]
    public void TheWordDrivesAFieldZeroOrSignExtended()
    {
        var machine = Build(Field16(), "LSLI R0, R1, 15\nHLT");
        machine.SingleStep();
        machine.SingleStep();
        var word = machine.Device<InstructionWord>("iw");

        Assert.Equal(machine.ProgramByteCode[0], word.Word);
        Assert.Equal((0, 1, 15, 0xFFFF), (word.Field(0), word.Field(1), word.Field(3), word.Field(3, signed: true)));
        Assert.Equal(7, word.Field(2));
        Assert.Contains("sfield3", word.SignalLines());
    }

    [Fact]
    public void FetchJumpsThroughTheMappingRomToTheInstructionsFirstStep()
    {
        var machine = Build(Field16(), "ADD R0, R1, R2\nHLT");
        machine.SingleStep();
        machine.SingleStep();

        var add = machine.DecoderRom.Blocks.Single(b => b.Instruction.Mnemonic == "ADD");
        Assert.Equal(add.Base, machine.MicroStepRegister);
        Assert.Equal(0, machine.DecoderRom.StepFor(0));
        Assert.Equal(0, machine.DecoderRom.StepFor(63 << 10));
        Assert.Equal(add.Base, machine.DecoderRom.StepFor(machine.ProgramByteCode[0]));
    }

    [Fact]
    public void AnAddReadsItsRegistersFromTheWordInSixTicks()
    {
        var machine = Build(Field16(), "MOVI R1, 20\nMOVI R2, 22\nADD R0, R1, R2\nHLT");
        machine.StepInstruction();
        machine.StepInstruction();
        machine.StepInstruction();
        int before = machine.Cycles;
        machine.StepInstruction();

        Assert.Equal(6 + 2, machine.Cycles - before);
        Assert.Equal(42, machine.Device<RegisterFile>("rf")[0]);
    }

    [Theory]
    [InlineData("Hello, world on the LCD")]
    [InlineData("Fibonacci on the LCD")]
    public void Field16PrintsWhatRisc16PrintsInFewerCellsAndTicks(string program)
    {
        var risc = Run(Risc16(), Risc16().Programs.Single(p => p.Name == program).Source);
        var field = Run(Field16(), Field16().Programs.Single(p => p.Name == program).Source);

        Assert.True(field.IsHalted);
        Assert.Equal(Lcd(risc).Replace("RISC-16", "FIELD-16"), Lcd(field));
        Assert.True(field.ProgramByteCode.Length < risc.ProgramByteCode.Length);
        Assert.True(field.Cycles < risc.Cycles);
    }

    [Fact]
    public void Field16DrawsTheSameGradientPixelForPixel()
    {
        const string program = "Colour gradient on the screen";
        var risc = Run(Risc16(), Risc16().Programs.Single(p => p.Name == program).Source);
        var field = Run(Field16(), Field16().Programs.Single(p => p.Name == program).Source);

        Assert.True(field.IsHalted);
        Assert.Equal(risc.Device<Framebuffer>("fb").Pixels, field.Device<Framebuffer>("fb").Pixels);
    }

    [Fact]
    public void ARewrittenWordDisassemblesFromItsFields()
    {
        var machine = Build(Field16(), "ADD R0, R1, R2\nMOVI R3, 7\nHLT");
        var memory = machine.Device<RamModule>("mem");
        memory.memory[0] = (ushort)(machine.ProgramByteCode[0] | (7 << 7));
        memory.memory[1] = (ushort)((3 << 10) | (4 << 7));

        var add = machine.InstructionAt(0);
        Assert.Equal("ADD", add.Mnemonic);
        Assert.Equal(new[] { "7", "1", "2" }, add.Operands);
        var movi = machine.InstructionAt(1);
        Assert.Equal("MOVI", movi.Mnemonic);
        Assert.Equal(new[] { "4", "0007" }, movi.Operands);
        Assert.Equal(2, movi.Cells.Length);
    }

    [Fact]
    public void TheWordIsDrawnBitByBit()
    {
        var microcode = Field16().Machine.Decoder.Microcode;
        var format = InstructionFormat.Of(microcode);

        Assert.Equal("000110 aaa bbb ccc -", format.Picture(microcode.FindInstruction("ADD")));
        Assert.Equal("010000 aaa bbb cccc", format.Picture(microcode.FindInstruction("LSLI")));
        Assert.Equal("a = operand 1, bits 9-7 · operand 2 in the next cell", format.Placement(microcode.FindInstruction("MOVI")));
        Assert.Equal("000000 ----------", format.Picture(microcode.Fetch));
    }

    [Fact]
    public void TheHoverDrawsTheWordAndSaysWhereEachOperandGoes()
    {
        var hover = new AssemblyLanguage(Field16().Machine.Decoder.Microcode, registerCount: 8).Hover("        MOVI R0, 5", 1, 10);

        Assert.Contains("opcode `0x0C00`", hover);
        Assert.Contains("`000011 aaa -------`", hover);
        Assert.Contains("a = operand 1, bits 9-7 · operand 2 in the next cell", hover);
    }

    private static IReadOnlyList<string> Problems(Action<MicrocodeDefinition> change)
    {
        var machine = Field16().Machine;
        change(machine.Decoder.Microcode);
        return MicrocodeValidator.Validate(machine.Decoder.Microcode, machine).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message).ToList();
    }

    [Fact]
    public void TheShippedFormatIsValid()
    {
        Assert.Empty(Problems(_ => { }));
    }

    [Fact]
    public void TheOpcodeMustHaveRoomForEveryInstructionAfterFetch()
    {
        Assert.Contains(Problems(m => m.OpcodeBits = 5), p => p.Contains("has 37 instructions, but an opcode of 5 bits has room for 31: opcode 0 is fetch."));
        Assert.Contains(Problems(m => m.OpcodeBits = 16), p => p.Contains("opcodeBits must be between 1 and 15"));
    }

    [Fact]
    public void FieldsMustLieBelowTheOpcode()
    {
        Assert.Contains(Problems(m => m.Fields[0].Low = 8), p => p.Contains("field 0 must lie in bits 9 to 0"));
        Assert.Contains(Problems(m => m.Fields.Add(new FieldDefinition { Low = 0, Bits = 1 })), p => p.Contains("has 5 fields"));
    }

    [Fact]
    public void AnInstructionUsesEachFieldOnceAndNeverTwoThatShareBits()
    {
        Assert.Contains(Problems(m => m.FindInstruction("ADD").Fields = new List<int> { 0, 0, 2 }), p => p.Contains("puts two operands in field 0."));
        Assert.Contains(Problems(m => m.FindInstruction("ADD").Fields = new List<int> { 0, 2, 3 }), p => p.Contains("fields 2 and 3, which share bits"));
        Assert.Contains(Problems(m => m.FindInstruction("ADD").Fields = new List<int> { 0, 1, 7 }), p => p.Contains("uses field 7, but the microcode defines 4 fields."));
        Assert.Contains(Problems(m => m.FindInstruction("OUT").Fields = new List<int> { 0, 1 }), p => p.Contains("puts 2 operands in fields, but takes 1 operand."));
    }

    [Fact]
    public void FieldsNeedADecoderThatReadsTheOpcodeFromTheWord()
    {
        Assert.Contains(Problems(m => m.OpcodeBits = null), p => p.Contains("has operand fields but no opcodeBits"));
    }

    [Fact]
    public void FormatSettingsSurviveTheJsonRoundTripAndStayOutWhenUnset()
    {
        var json = Field16().Machine.ToJson();
        var again = MachineDefinition.FromJson(json).Decoder.Microcode;

        Assert.Equal(6, again.OpcodeBits);
        Assert.Equal(new[] { (7, 3), (4, 3), (1, 3), (0, 4) }, again.Fields.Select(f => (f.Low, f.Bits)));
        Assert.Equal(new[] { 0, 1, 3 }, again.FindInstruction("LSLI").Fields);
        Assert.DoesNotContain("opcodeBits", Risc16().Machine.ToJson());
        Assert.DoesNotContain("\"fields\"", Risc16().Machine.ToJson());
    }
}
