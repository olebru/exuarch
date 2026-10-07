using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

// Corners of the lexer and the line grammar, read through the parser.
public class AssemblyGrammarTests
{
    private static string Kinds(string text) => string.Join(" ", AssemblyParser.ParseLine(text, 1).Tokens.Select(t => t.Kind));
    private static string Errors(string text) => string.Join(" | ", AssemblyParser.ParseLine(text, 1).SyntaxErrors.Select(d => $"{d.StartColumn}-{d.EndColumn} {d.Message}"));

    [Theory]
    [InlineData("loop: LAI 1, 'A' ; c", "Label Mnemonic Number Comma Character Comment")]
    [InlineData(".DATA \"hi\", -1, x", "Directive String Comma Number Comma LabelReference")]
    [InlineData("LAI #-5", "Mnemonic Error Number")]
    [InlineData("LAI 1$2", "Mnemonic Number Error")]
    [InlineData("LAI ?x", "Mnemonic Error")]
    public void TokensTakeTheirRoleFromWhereTheyStand(string text, string kinds)
    {
        Assert.Equal(kinds, Kinds(text));
    }

    [Theory]
    [InlineData("LAI -40000", "5-11 '-40000' does not fit in 16 bits, the lowest is -32768")]
    [InlineData("LAI #", "5-6 '#' is not a number, write 123, 0x7B, -5 or 'A'")]
    [InlineData("LAI '\\", "5-7 missing closing '")]
    [InlineData(".DATA \"a\\q\"", "7-11 unknown escape '\\q' | 11-12 missing closing \"")]
    [InlineData(".DATA \"Ā\"", "7-9 'Ā' is not a Latin-1 character | 9-10 missing closing \"")]
    [InlineData("  , LAI", "3-4 unexpected ',' | 3-4 missing operand after ','")]
    [InlineData("LAI 1,, 2", "7-8 unexpected ','")]
    [InlineData("x: y: NOP", "4-6 a label must come first on the line")]
    [InlineData("1 LAI 2", "1-2 '1' needs a mnemonic before it")]
    public void ProblemsAreFoundWhileReadingTheLine(string text, string errors)
    {
        Assert.Equal(errors, Errors(text));
    }

    [Fact]
    public void ANegativeNumberIsStoredAsItsTwosComplement()
    {
        Assert.Equal(new[] { 0xFFFF }, AssemblyParser.ParseLine("LAI -1", 1).Operands[0].Values);
    }

    [Fact]
    public void DirectivesAreReadInAnyCase()
    {
        var result = new AssemblyLanguage(MicrocodeDefinition.FromJson(ExampleData.MICROCODE)).Analyze(".string \"ab\"\n.Byte 3");
        Assert.Equal(new[] { 'a', 'b', 0, 3 }, result.Cells);
        Assert.Equal(".BYTE is an old name for .DATA: every value takes one 16 bit cell either way. Use .DATA, or .STRING for text that ends with 0.", Assert.Single(result.Warnings).Message);
    }
}
