using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class AddressingModeTests
{
    private const string Instructions = """
        { "mnemonic": "HLT", "operands": 0, "steps": [ { "signals": ["clk.disable"] } ] },
        { "mnemonic": "LDI", "operandTypes": ["register", "value"], "syntax": "LD {0}, {1}", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDA", "operandTypes": ["register", "address"], "syntax": "LD {0}, [{1}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDN", "operandTypes": ["register", "register"], "syntax": "LD {0}, [{1}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDX", "operandTypes": ["register", "register", "value"], "syntax": "LD {0}, [{1} + {2}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDY", "operandTypes": ["register", "register", "register"], "syntax": "LD {0}, [{1} + {2}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDP", "operandTypes": ["register", "register"], "syntax": "LD {0}, [{1}]+", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "LDC", "operandTypes": ["register", "relative"], "syntax": "LD {0}, [PC + {1}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "STX", "operandTypes": ["register", "value", "register"], "syntax": "ST {2}, [{0} + {1}]", "steps": [ { "signals": ["ir.reset"] } ] },
        { "mnemonic": "BRA", "operandTypes": ["relative"], "steps": [ { "signals": ["ir.reset"] } ] }
        """;

    private static MicrocodeDefinition Microcode(string instructions = Instructions, string extra = "") => MicrocodeDefinition.FromJson($$"""
        { "fetch": { "mnemonic": "FETCH", "steps": [ { "signals": ["pc.output", "mem.loadmar"] }, { "signals": ["mem.output", "ir.load", "pc.inc"] } ] },
          {{extra}}
          "instructions": [ {{instructions}} ] }
        """);

    private static AssemblyLanguage Language(MicrocodeDefinition microcode = null) => new AssemblyLanguage(microcode ?? Microcode(), 4096, 8);

    private static readonly MachineDefinition Machine = BuiltInPackages.Get("RISC-16").Machine;

    private static int Op(string mnemonic) => new DecoderRom(Microcode()).FetchByteCodeFromMnemonic(mnemonic);

    private static int[] Cells(string source)
    {
        var result = Language().Analyze(source);
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics));
        return result.Cells;
    }

    private static string Error(string source)
    {
        var result = Language().Analyze(source);
        Assert.False(result.Success);
        return result.Errors.First().Message;
    }

    [Theory]
    [InlineData("LD R0, 5", "LDI", new[] { 0, 5 })]
    [InlineData("LD R0, [R1]", "LDN", new[] { 0, 1 })]
    [InlineData("LD R0, [R1 + 4]", "LDX", new[] { 0, 1, 4 })]
    [InlineData("LD R0, [R1 - 4]", "LDX", new[] { 0, 1, 0xFFFC })]
    [InlineData("LD R0, [R1+4]", "LDX", new[] { 0, 1, 4 })]
    [InlineData("LD R0, [R1 + R2]", "LDY", new[] { 0, 1, 2 })]
    [InlineData("LD R0, [R1]+", "LDP", new[] { 0, 1 })]
    [InlineData("ld r3, [r1 + 'A']", "LDX", new[] { 3, 1, 65 })]
    [InlineData("ST R0, [R2 + 3]", "STX", new[] { 2, 3, 0 })]
    public void EachWrittenFormAssemblesToItsInstruction(string line, string instruction, int[] operands)
    {
        Assert.Equal(new[] { Op(instruction) }.Concat(operands), Cells(line));
    }

    [Fact]
    public void BracketsAroundALabelReadTheCellAtItsAddress()
    {
        Assert.Equal(new[] { Op("LDA"), 0, 4, Op("HLT"), 77 }, Cells("LD R0, [table]\nHLT\ntable: .DATA 77"));
        Assert.Equal(new[] { Op("LDA"), 0, 0x100 }, Cells("LD R0, [0x100]"));
    }

    [Fact]
    public void TheOldNamesStillAssembleToTheSameCells()
    {
        Assert.Equal(Cells("LD R0, [R1 + 4]"), Cells("LDX R0, R1, 4"));
        Assert.Equal(Cells("ST R0, [R2 + 3]"), Cells("STX R2, 3, R0"));
    }

    [Fact]
    public void ARelativeOperandIsTheDistanceFromTheEndOfTheInstruction()
    {
        Assert.Equal(new[] { Op("BRA"), 0, Op("HLT") }, Cells("BRA next\nnext: HLT"));
        Assert.Equal(new[] { Op("HLT"), Op("BRA"), 0xFFFD }, Cells("back: HLT\nBRA back"));
        Assert.Equal(new[] { Op("LDC"), 0, 1, Op("HLT"), 9 }, Cells("LD R0, [PC + data]\nHLT\ndata: .DATA 9"));
        Assert.Equal(new[] { Op("LDC"), 0, 8 }, Cells("LD R0, [PC + 8]"));
        Assert.Equal(new[] { Op("LDC"), 0, 0xFFFE }, Cells("LD R0, [pc - 2]"));
    }

    [Theory]
    [InlineData("LD R0, [R1 - R2]", "no form of LD matches these operands. LD is written LD register, value · LD register, [address]")]
    [InlineData("LD R0, [R1 * 2]", "unexpected '*'")]
    [InlineData("LD R0, [R1", "missing ']' at the end of the line")]
    [InlineData("LD R0, [R1, 2]", "missing ']' before ','")]
    [InlineData("LD R0, R1 + 4", "'+' is only allowed inside [ ], as in [R1 + 4]")]
    [InlineData("LD R0, [R1 + ]", "missing a register, number or label before ']'")]
    [InlineData("LD R0, [R1 4]", "missing '+' or '-' before '4'")]
    [InlineData("LDX R0, [R1 + 4]", "LDX takes its operands as they are, without [ ]: the instruction has no syntax with brackets")]
    [InlineData(".DATA [1]", ".DATA stores values as they are: [ ] is only for instruction operands")]
    [InlineData("LD R0, [nowhere]", "unknown label 'nowhere'")]
    [InlineData("MOV R0, [R1]", "unknown mnemonic 'MOV'")]
    public void AMistakeInAWrittenFormIsReportedWhereItIs(string source, string expected)
    {
        Assert.StartsWith(expected, Error(source));
    }

    [Fact]
    public void AnUnmatchedLineKeepsTheAddressesAfterItInPlace()
    {
        var result = Language().Analyze("LD R0, [R1 - R2]\nhere: HLT");
        Assert.Equal(4, result.Labels["here"]);
    }

    [Fact]
    public void FormattingSpacesTheBracketsAndSpellsTheMnemonicAsTheSyntaxDoes()
    {
        var language = Language();
        Assert.Equal("        LD    R0, [R1 + 4]\n        LD    R0, [R1 - 4]\n        LD    R2, [R3]+", language.FormatDocument("ld R0,[R1+4]\n  Ld R0,[ R1 -4 ]\nLD R2,[R3]+"));
        Assert.Equal("        LDX   R0, R1, 4", language.FormatDocument("ldx R0, R1, 4"));
    }

    [Fact]
    public void TheListingShowsTheOperandsAsWritten()
    {
        var result = Language().Analyze("LD R0, [R1 - 4]");
        Assert.Equal(new[] { "R0", "[R1 - 4]" }, result.Listing.Single().Operands);
    }

    [Fact]
    public void HoverNamesTheFormAndTheInstructionBehindIt()
    {
        var hover = Language().Hover("LD R0, [R1 + 4]", 1, 2);
        Assert.StartsWith("`LD register, [register + value]` · a form of **LDX**", hover);
        Assert.Contains("**relative** for LDC", Language().Hover("LD R0, [PC + 8]", 1, 15));
    }

    [Fact]
    public void CompletionOffersTheWrittenMnemonicAndWhatFitsInsideTheBrackets()
    {
        var language = Language();
        var mnemonics = language.Complete("", 1, 1);
        var ld = Assert.Single(mnemonics, item => item.Label == "LD");
        Assert.Equal("7 forms", ld.Detail);
        Assert.Contains("- `LD register, [register + register]` · LDY", ld.Documentation);
        Assert.Contains(language.Complete("LD R0, [R1 + R2]", 1, 15), item => item.Label == "R3");
        Assert.Contains(language.Complete("LD R0, [", 1, 9), item => item.Label == "R1");
        Assert.Contains("LD", language.Mnemonics);
    }

    [Theory]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["register"], "syntax": "LD {0}, {1}", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax 'LD {0}, {1}' uses {1}, but it has 1 operand, numbered from {0}")]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["register", "value"], "syntax": "LD {0}", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax 'LD {0}' leaves out {1}: every operand must appear once")]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["register"], "syntax": "LD {0}, {0}", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax 'LD {0}, {0}' uses {0} more than once")]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["register", "value"], "syntax": "LD {0}, [{0} - {1}]", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax 'LD {0}, [{0} - {1}]' uses '-': write '+', and a program can then write either")]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["value"], "syntax": "LD [{0} + 2]", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax 'LD [{0} + 2]' has '2': write {0}, {1} and so on for operands, or a fixed word like PC")]
    [InlineData("""{ "mnemonic": "A", "operandTypes": ["value"], "syntax": "[{0}]", "steps": [ { "signals": ["ir.reset"] } ] }""", "its syntax '[{0}]' does not parse: '[' needs a mnemonic before it")]
    public void ASyntaxThatCanNotWorkIsAMicrocodeError(string instruction, string expected)
    {
        var diagnostics = MicrocodeValidator.Validate(Microcode(instruction), Machine);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Instruction == "A" && d.Message == expected + ".");
    }

    [Fact]
    public void TwoInstructionsWithTheSameSyntaxAreAMicrocodeError()
    {
        var twice = Instructions + """, { "mnemonic": "LDZ", "operandTypes": ["register", "register", "value"], "syntax": "ld {0}, [{1} + {2}]", "steps": [ { "signals": ["ir.reset"] } ] }""";
        var diagnostics = MicrocodeValidator.Validate(Microcode(twice), Machine);
        Assert.Contains(diagnostics, d => d.Instruction == "LDZ" && d.Message == "its syntax 'ld register, [register + value]' is the same as LDX's, so every line that matches one matches both.");
        Assert.DoesNotContain(MicrocodeValidator.Validate(Microcode(), Machine), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void APlainSyntaxNamedLikeAnotherInstructionNeverMatches()
    {
        var shadowed = Instructions + """, { "mnemonic": "OTHER", "operandTypes": ["relative"], "syntax": "BRA {0}", "steps": [ { "signals": ["ir.reset"] } ] }""";
        var diagnostics = MicrocodeValidator.Validate(Microcode(shadowed), Machine);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Instruction == "OTHER" && d.Message.StartsWith("its syntax 'BRA relative' never matches"));
    }

    [Fact]
    public void ARelativeOperandInAFieldIsCheckedAsASignedDistance()
    {
        var fielded = """{ "mnemonic": "HLT", "operands": 0, "steps": [ { "signals": ["clk.disable"] } ] }, { "mnemonic": "BR", "operandTypes": ["relative"], "fields": [0], "steps": [ { "signals": ["ir.reset"] } ] }""";
        var microcode = Microcode(fielded, """ "opcodeBits": 6, "fields": [ { "low": 0, "bits": 4 } ], """);
        var language = new AssemblyLanguage(microcode, 4096, 0);
        var back = language.Analyze("top: HLT\nBR top");
        Assert.True(back.Success, string.Join(" | ", back.Diagnostics));
        Assert.Equal((2 << 10) | 0xE, back.Cells[1]);
        var far = language.Analyze("BR far\n.DATA 0, 0, 0, 0, 0, 0, 0, 0, 0, 0\nfar: HLT");
        Assert.Contains(far.Errors, e => e.Message == "'far' is 10 cells away, which does not fit in the 4 bit field at bits 3-0: it holds a distance from -8 to 7");
    }

    [Fact]
    public void RelativeIsAnOperandTypeInTheMachineFile()
    {
        var microcode = Microcode();
        Assert.Equal(OperandType.Relative, microcode.FindInstruction("BRA").OperandTypes[0]);
        Assert.Contains("\"relative\"", microcode.ToJson());
        Assert.Equal("LD {0}, [{1} + {2}]", MicrocodeDefinition.FromJson(microcode.ToJson()).FindInstruction("LDX").Syntax);
        Assert.DoesNotContain("\"syntax\"", MicrocodeDefinition.FromJson("""{ "instructions": [ { "mnemonic": "A", "steps": [] } ] }""").ToJson());
    }
}
