using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class AssemblyLanguageTests
{
    private static readonly MicrocodeDefinition Microcode = MicrocodeDefinition.FromJson(ExampleData.MICROCODE);
    private static AssemblyLanguage Language() => new AssemblyLanguage(Microcode);
    private static int Op(string mnemonic) => new DecoderRom(Microcode).FetchByteCodeFromMnemonic(mnemonic);

    [Fact]
    public void RelaxedAndTabSeparatedSyntaxAssembleTheSame()
    {
        var relaxed = Language().Analyze("start: LAI #1   ; comment\n  JMP start");
        var tabs = Language().Analyze("start:\tLAI\t#1\n\tJMP\tstart");
        Assert.True(relaxed.Success);
        Assert.Equal(tabs.Cells, relaxed.Cells);
        Assert.Equal(new[] { Op("LAI"), 1, Op("JMP"), 0 }, relaxed.Cells);
    }

    [Fact]
    public void LiteralsCharactersAndStrings()
    {
        var result = Language().Analyze("LAI #0x2A\nLBI #'Æ'\ndata: .DATA \"Hi\\n\", #'\\'', #7");
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics));
        Assert.Equal(new[] { Op("LAI"), 42, Op("LBI"), 198, 'H', 'i', 10, '\'', 7 }, result.Cells);
    }

    [Fact]
    public void EveryProblemIsReportedWithItsPosition()
    {
        var result = Language().Analyze("FOO #1\nLAI\nJMP nowhere\ndup: NOP\ndup: NOP\nLAI #70000\nLAI #1 #2\nLAI \"no\"");
        Assert.Collection(result.Diagnostics,
            d => Assert.Equal((1, 1, 4, "unknown mnemonic 'FOO'"), (d.Line, d.StartColumn, d.EndColumn, d.Message)),
            d => Assert.Equal((2, 1, "LAI takes 1 operand, found 0"), (d.Line, d.StartColumn, d.Message)),
            d => Assert.Equal((3, 5, 12, "unknown label 'nowhere'"), (d.Line, d.StartColumn, d.EndColumn, d.Message)),
            d => Assert.Equal((5, 1, "label 'dup' is defined more than once"), (d.Line, d.StartColumn, d.Message)),
            d => Assert.Equal((6, 5, "'#70000' is not a number between 0 and 65535"), (d.Line, d.StartColumn, d.Message)),
            d => Assert.Equal((7, 1, "LAI takes 1 operand, found 2"), (d.Line, d.StartColumn, d.Message)),
            d => Assert.Equal((7, 8, "missing ',' before '#2'"), (d.Line, d.StartColumn, d.Message)),
            d => Assert.Equal((8, 5, "a string is only allowed in .DATA and .STRING"), (d.Line, d.StartColumn, d.Message)));
    }

    [Theory]
    [InlineData("LAI #12z", "is not a number")]
    [InlineData("LAI #'ab'", "exactly one character")]
    [InlineData(".STRING \"open", "missing closing \"")]
    [InlineData("LAI #1,", "missing operand after ','")]
    [InlineData("#1", "needs a mnemonic before it")]
    [InlineData("NOP x: y", "a label must come first")]
    public void SyntaxErrors(string source, string expected)
    {
        Assert.Contains(Language().Analyze(source).Diagnostics, d => d.Message.Contains(expected));
    }

    [Fact]
    public void CompletionListsInstructionsAtTheStartAndLabelsInOperands()
    {
        var source = "loop: NOP\n  L\nend: JMP ";
        var start = Language().Complete(source, 2, 4);
        var lai = Assert.Single(start, i => i.Label == "LAI");
        Assert.Equal("value", lai.Detail);
        Assert.Equal(Microcode.Instructions.Single(i => i.Mnemonic == "LAI").Description, lai.Documentation);
        Assert.Equal("LAI ", lai.InsertText);
        Assert.Contains(start, i => i.Label == ".DATA" && i.Kind == CompletionKind.Directive);
        Assert.Contains(start, i => i.Label == ".STRING" && i.Kind == CompletionKind.Directive);
        Assert.DoesNotContain(start, i => i.Label == ".BYTE" || i.Label == ".WORD");
        Assert.DoesNotContain(start, i => i.Label == "FTC");

        var operands = Language().Complete(source, 3, 10);
        Assert.Equal(new[] { "loop", "end" }, operands.Select(i => i.Label));
        Assert.All(operands, i => Assert.Equal(CompletionKind.Label, i.Kind));

        Assert.Empty(Language().Complete("NOP ", 1, 5));
        Assert.Empty(Language().Complete("NOP ; L", 1, 8));
    }

    [Fact]
    public void HoverDescribesInstructionsLabelsAndNumbers()
    {
        var source = "loop: JEQ loop\n LAI #65";
        var jeq = Language().Hover(source, 1, 8);
        Assert.Contains("**JEQ** address", jeq);
        Assert.Contains(Microcode.Instructions.Single(i => i.Mnemonic == "JEQ").Description, jeq);
        Assert.Contains("when Z=1", jeq);
        Assert.Equal("label **loop** at `0x0000` (0)\n\n**address** for JEQ: the memory location it uses", Language().Hover(source, 1, 12));
        Assert.Equal("`65` · `0x0041` · 'A'\n\n**value** for LAI: used as it is", Language().Hover(source, 2, 7));
        Assert.Equal("label **loop** at `0x0000` (0)", Language().Hover(source, 1, 2));
        Assert.Contains("not an instruction", Language().Hover("XYZ", 1, 2));
        Assert.Null(Language().Hover(source, 1, 20));
    }

    [Fact]
    public void FormattingAlignsColumnsAndKeepsTheProgram()
    {
        var messy = "; title\nstart:LAI #1;one\n   LBI   #2\nlonglabel: ADD\n\tJMP\tstart   ;   back\n  ; indented note";
        var formatted = AssemblyLanguage.Format(messy);
        Assert.Equal(string.Join("\n",
            "; title",
            "start:     LAI   1           ; one",
            "           LBI   2",
            "longlabel: ADD",
            "           JMP   start       ; back",
            "           ; indented note"), formatted);
        Assert.Equal(AssemblyLanguage.Format(formatted), formatted);
        Assert.Equal(Language().Analyze(messy).Cells, Language().Analyze(formatted).Cells);
    }

    [Fact]
    public void FormattingLeavesBrokenLinesAlone()
    {
        var source = "LAI #1\n  LBI #'ab'  ";
        Assert.Equal("        LAI   1\n  LBI #'ab'  ", AssemblyLanguage.Format(source));
    }

    [Fact]
    public void FormatLineUsesTheWholeDocumentsColumns()
    {
        Assert.Equal("        LAI   1", AssemblyLanguage.FormatLine("start: NOP\nLAI #1", 2));
        Assert.Equal("longer_label: LAI   1", AssemblyLanguage.FormatLine("longer_label: LAI #1\nNOP", 1));
    }

    [Fact]
    public void ExamplesAreFormattedAndAssemble()
    {
        foreach (var (name, source) in ExampleData.Programs)
        {
            Assert.Equal(AssemblyLanguage.Format(source), source);
            Assert.True(Language().Analyze(source).Success, name);
        }
    }

    [Fact]
    public void LanguageStillWorksWhenTheMicrocodeDoesNotCompile()
    {
        var broken = MicrocodeDefinition.FromJson(ExampleData.MICROCODE);
        broken.Instructions.Add(new InstructionDefinition { Mnemonic = "BAD", Steps = { new MicroStep { Signals = { "not a signal" } } } });
        var language = new AssemblyLanguage(broken);
        Assert.True(language.Analyze("LAI #1\nBAD").Success);
        Assert.Contains(language.Complete("", 1, 1), i => i.Label == "BAD");
    }

    [Fact]
    public void MnemonicsAreCaseInsensitiveAndFormattingNormalisesThem()
    {
        var language = Language();
        var lower = language.Analyze("start: lai #1\n  jmp start\n  .data #2");
        Assert.True(lower.Success, string.Join(" | ", lower.Diagnostics));
        Assert.Equal(language.Analyze("start: LAI #1\nJMP start\n.DATA #2").Cells, lower.Cells);
        Assert.Contains("**LAI**", language.Hover("lai #1", 1, 2));
        Assert.Equal("start:  LAI    1\n        JMP    start\n        .DATA  2", language.FormatDocument("start: lai #1\n  jmp start\n  .data #2"));
        Assert.Equal("        LAI   1", language.FormatDocumentLine("start: nop\nlai #1", 2));
    }

    [Fact]
    public void StringEndsWithAZeroCell()
    {
        var result = Language().Analyze("msg: .STRING \"Hi\", 10\nnext: .DATA 7, msg");
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics));
        Assert.Equal(new[] { 'H', 'i', 10, 0, 7, 0 }, result.Cells);
        Assert.Equal(4, result.Labels["next"]);
        Assert.Equal(new[] { 0 }, Language().Analyze(".STRING \"\"").Cells);
        Assert.Contains("0 cell", Language().Hover(".STRING \"x\"", 1, 3));
    }

    [Fact]
    public void ByteAndWordAreOldNamesForData()
    {
        var old = Language().Analyze("a: .BYTE \"ok\", 1\nb: .word 0xBEEF, a");
        Assert.True(old.Success, string.Join(" | ", old.Diagnostics));
        Assert.Equal(Language().Analyze("a: .DATA \"ok\", 1\nb: .DATA 0xBEEF, a").Cells, old.Cells);
        Assert.Equal(2, old.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("old name for .DATA")));
        Assert.Contains("old name", Language().Hover(".WORD 1", 1, 3));
        Assert.Contains(Language().Analyze(".DWORD 1").Diagnostics, d => d.Message == "unknown directive '.DWORD', use .DATA or .STRING");
    }

    [Fact]
    public void LiteralsNeedNoHash()
    {
        var bare = Language().Analyze("LAI 15\nLBI 0x2A\nDWI 'Æ'\nLDA 3\ndata: .DATA 1, 'x', \"ok\"");
        var hashed = Language().Analyze("LAI #15\nLBI #0x2A\nDWI #'Æ'\nLDA #3\ndata: .DATA #1, #'x', \"ok\"");
        Assert.True(bare.Success, string.Join(" | ", bare.Diagnostics));
        Assert.Equal(hashed.Cells, bare.Cells);
        Assert.Contains(Language().Analyze("LAI 12z").Diagnostics, d => d.Message.Contains("is not a number"));
    }

    [Fact]
    public void FormattingDropsTheHash()
    {
        Assert.Equal("        LAI   15\n        DWI   'A'", AssemblyLanguage.Format("LAI #15\nDWI #'A'"));
        Assert.All(ExampleData.Programs, p => Assert.DoesNotContain("#", p.Source.Split('\n').Select(l => l.Split(';')[0]).Aggregate("", (a, b) => a + b)));
    }

    [Fact]
    public void LabelWhereAValueIsExpectedIsAWarning()
    {
        var result = Language().Analyze("loop: LAI loop\n      LDA loop\n      JMP loop");
        Assert.True(result.Success);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal((1, 11, 15), (warning.Line, warning.StartColumn, warning.EndColumn));
        Assert.Contains("LAI takes a value here, and 'loop' is the address of a label", warning.Message);
        new Assembler(new DecoderRom(Microcode)).Assemble("loop: LAI loop");
    }

    [Fact]
    public void SignaturesComeFromOperandTypes()
    {
        var lda = Microcode.FindInstruction("LDA");
        Assert.Equal(new[] { OperandType.Address }, lda.OperandTypes);
        Assert.Equal("LDA address", lda.Signature);
        Assert.Equal("LAI value", Microcode.FindInstruction("LAI").Signature);
        Assert.Equal("NOP", Microcode.FindInstruction("NOP").Signature);
        Assert.Contains("**LDA** address", Language().Hover("LDA 3", 1, 2));
        Assert.Contains("**NOP** · no operands", Language().Hover("NOP", 1, 2));
        Assert.All(Microcode.Instructions.Where(i => i.OperandCount > 0), i => Assert.NotNull(i.OperandTypes));
    }

    [Fact]
    public void OperandTypesMustMatchTheOperandCount()
    {
        var microcode = MicrocodeDefinition.FromJson(ExampleData.MICROCODE);
        microcode.FindInstruction("LDA").OperandTypes = new System.Collections.Generic.List<OperandType> { OperandType.Address, OperandType.Value };
        var error = Assert.Single(MicrocodeValidator.Validate(microcode, MachineDefinition.FromJson(ExampleData.MACHINE)), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("declares 1 operand but 2 operand types", error.Message);

        var inferred = new InstructionDefinition { Mnemonic = "X", OperandTypes = new System.Collections.Generic.List<OperandType> { OperandType.Value, OperandType.Value } };
        Assert.Equal(2, inferred.OperandCount);
    }

    [Fact]
    public void NegativeNumbersAreTwosComplement()
    {
        var result = Language().Analyze("        .DATA -1, -2, -32768, -0x10, 7");
        Assert.True(result.Success, string.Join(" | ", result.Diagnostics));
        Assert.Equal(new[] { 0xFFFF, 0xFFFE, 0x8000, 0xFFF0, 7 }, result.Cells);
        Assert.Contains(Language().Analyze("        .DATA -40000").Diagnostics, d => d.Message.Contains("the lowest is -32768"));
        Assert.False(Language().Analyze("        .DATA - 1").Success);
    }
}
