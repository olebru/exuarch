using System.Collections.Generic;
using System.Linq;
using Exuarch.Core.SharpC;

namespace Exuarch.Core.Tests.SharpC;

public class SharpLanguageTests
{
    private static readonly SharpProgramSymbols Symbols = new SharpProgramSymbols(
        new[]
        {
            new SharpClassSymbol("Node", 1,
                new[] { new SharpFieldSymbol("value", "Node", "int", false, false, 3) },
                new[] { new SharpMethodSymbol("Insert", "Node", "void", new[] { new SharpParameterSymbol("int", "v") }, false, 5) },
                new SharpMethodSymbol("Node", "Node", "void", new[] { new SharpParameterSymbol("int", "v") }, false, 4)),
        },
        new[] { new SharpMethodSymbol("Fib", "Program", "int", new[] { new SharpParameterSymbol("int", "n") }, true, 10) },
        new[] { new SharpFieldSymbol("Size", "Program", "int", true, true, 9) });

    [Fact]
    public void TheExamplesAreFormattedAlready()
    {
        foreach (var example in Sharp16.Examples)
            Assert.Equal(example.CSharp.TrimEnd('\n'), SharpFormatter.Format(example.CSharp).TrimEnd('\n'));
    }

    [Fact]
    public void FormattingIndentsBlocksCasesAndBodiesWithoutBraces()
    {
        const string messy = "class A\n{\nstatic void Main()\n{\nfor (int i = 0; i < 3; i++)\nif (i > 1)\nConsole.Write(i);\nswitch (x)\n{\ncase 1:\nConsole.Write(\"{ not a block\");\nbreak;\ndefault:\nbreak;\n}\nint y = F(1,\n2);\n   }   \n}";
        const string tidy = "class A\n{\n    static void Main()\n    {\n        for (int i = 0; i < 3; i++)\n            if (i > 1)\n                Console.Write(i);\n        switch (x)\n        {\n            case 1:\n                Console.Write(\"{ not a block\");\n                break;\n            default:\n                break;\n        }\n        int y = F(1,\n            2);\n    }\n}";
        Assert.Equal(tidy, SharpFormatter.Format(messy));
        Assert.Equal(tidy, SharpFormatter.Format(tidy));
        Assert.Equal("        for (int i = 0; i < 3; i++)", SharpFormatter.FormatLine(messy, 5));
    }

    [Fact]
    public void FormattingLeavesCommentsAndVerbatimStringsInside()
    {
        const string source = "class A\n{\n/* a\n      kept\n*/\nstring s = @\"one\n   two\";\n}";
        var lines = SharpFormatter.Format(source).Split('\n');
        Assert.Equal("    /* a", lines[2]);
        Assert.Equal("      kept", lines[3]);
        Assert.Equal("   two\";", lines[6]);
        Assert.Equal(8, lines.Length);
    }

    [Fact]
    public void AfterALibraryClassOnlyItsMembersAreSuggested()
    {
        var items = SharpLanguageService.Complete("Screen.", 1, 8, null);
        Assert.Equal(new[] { "Clear", "Plot", "MoveTo", "Put", "Rgb" }, items.Select(i => i.Label));
        Assert.Contains(SharpLanguageService.Complete("x = Keys.L", 1, 11, null), i => i.Label == "Left" && i.Kind == SharpCompletionKind.Constant);
    }

    [Fact]
    public void SuggestionsIncludeTheProgramsOwnSymbolsAndWords()
    {
        var items = SharpLanguageService.Complete("int counter = 0;\nco", 2, 3, Symbols);
        Assert.Contains(items, i => i.Label == "Node" && i.Kind == SharpCompletionKind.Class);
        Assert.Contains(items, i => i.Label == "Fib" && i.Kind == SharpCompletionKind.Method);
        Assert.Contains(items, i => i.Label == "counter" && i.Kind == SharpCompletionKind.Variable);
        Assert.Contains(items, i => i.Label == "foreach" && i.IsSnippet);
        Assert.Contains(SharpLanguageService.Complete("Program.", 1, 9, Symbols), i => i.Label == "Size");
        Assert.Contains(SharpLanguageService.Complete("node.", 1, 6, Symbols), i => i.Label == "Insert");
        Assert.Empty(SharpLanguageService.Complete("// Screen.", 1, 11, Symbols));
        Assert.Empty(SharpLanguageService.Complete("Console.Write(\"Screen.", 1, 23, Symbols));
    }

    [Fact]
    public void HoverDescribesLibraryMembersTypesAndTheProgramsSymbols()
    {
        Assert.Contains("int Screen.Rgb(int r, int g, int b)", SharpLanguageService.Hover("int c = Screen.Rgb(1, 2, 3);", 1, 17, null));
        Assert.Contains("-32768", SharpLanguageService.Hover("int x;", 1, 2, null));
        Assert.Contains("static int Program.Fib(int n)", SharpLanguageService.Hover("Fib(3);", 1, 2, Symbols));
        Assert.Contains("int Node.value", SharpLanguageService.Hover("class Node", 1, 8, Symbols));
        Assert.Null(SharpLanguageService.Hover("    ", 1, 2, Symbols));
    }

    [Fact]
    public void SignatureHelpFindsTheCallAndTheArgument()
    {
        var plot = SharpLanguageService.SignatureHelp("Screen.Plot(x, (y + 1), ", 1, 25, null);
        Assert.Equal("void Screen.Plot(int x, int y, int colour)", plot.Signatures.Single().Label);
        Assert.Equal(2, plot.ActiveParameter);
        Assert.Equal(2, SharpLanguageService.SignatureHelp("Console.Write(\"a, b\"", 1, 21, null).Signatures.Count);
        Assert.Equal("new Node(int v)", SharpLanguageService.SignatureHelp("var n = new Node(", 1, 18, Symbols).Signatures.Single().Label);
        Assert.Equal(0, SharpLanguageService.SignatureHelp("Fib(\n", 2, 1, Symbols).ActiveParameter);
        Assert.Null(SharpLanguageService.SignatureHelp("Fib(1);", 1, 8, Symbols));
    }
}
