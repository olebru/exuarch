using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Exuarch.Core;
using Exuarch.Core.SharpC;

namespace Exuarch.Core.Tests.SharpC;

public class SharpCompilerTests
{
    private static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

    private static string CasesFolder => Path.Combine(Folder(), "Cases");

    public static IEnumerable<object[]> Cases()
    {
        return Directory.EnumerateFiles(CasesFolder, "*.csharp").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, System.StringComparer.Ordinal).Select(n => new object[] { n });
    }

    private static string Render(SharpCompilation result)
    {
        if (!result.Ok) return $"error {result.Error.Line}:{result.Error.Col} {result.Error.Message}\n";
        var lines = result.Lines.Select(l => $"{l.SourceLine}|{l.Text}");
        return $"runtime {string.Join(" ", result.Runtime)}\n" + string.Join("\n", lines) + "\n";
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ACaseCompilesToItsExpectedOutput(string name)
    {
        var source = File.ReadAllText(Path.Combine(CasesFolder, name + ".csharp"));
        var expectedFile = Path.Combine(CasesFolder, name + ".expected");
        var actual = Render(SharpCompiler.Compile(source));
        if (Snapshots.Snapshot.Accepting) File.WriteAllText(expectedFile, actual);
        var expected = File.ReadAllText(expectedFile);
        if (actual == expected) return;
        var want = expected.Split('\n');
        var got = actual.Split('\n');
        int line = Enumerable.Range(0, System.Math.Max(want.Length, got.Length)).First(i => i >= want.Length || i >= got.Length || want[i] != got[i]);
        Assert.Fail($"{name}: line {line + 1} differs\n  expected: {(line < want.Length ? want[line] : "<end>")}\n  actual:   {(line < got.Length ? got[line] : "<end>")}");
    }

    [Fact]
    public void TheAssemblyIsTheLinesJoined()
    {
        var result = SharpCompiler.Compile(Sharp16.Examples[0].CSharp);
        Assert.True(result.Ok);
        Assert.Equal(string.Join("\n", result.Lines.Select(l => l.Text)) + "\n", result.Assembly);
    }

    [Fact]
    public void APackageMatchesTheOneTheJavaScriptBuilt()
    {
        var probe = MachinePackage.FromJson(File.ReadAllText(Path.Combine(Folder(), "package-probe.json")));
        var made = Sharp16.MakePackage(new[]
        {
            new SharpPackageProgram("Mine", "Compiled from C# in SharpC", "  first line  \r\nsecond\n\n", "        HLT\n\n", new ProgramNeeds { Keypad = true, MinHz = 50000 }),
            new SharpPackageProgram("Sierpinski on the screen", "d", "x", "y", null),
        });
        Assert.Equal(probe.ToJson(), made.ToJson());
    }

    [Fact]
    public void EveryExampleBuildsAndAssemblesOnTheMachine()
    {
        var registry = DeviceRegistry.CreateDefault();
        var definition = Sharp16.Machine();
        Assert.Empty(Machine.ValidateDefinition(definition, registry));
        Assert.DoesNotContain(MicrocodeValidator.Validate(definition.Decoder.Microcode, definition, registry), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(7, Sharp16.Examples.Count);
        foreach (var example in Sharp16.Examples)
        {
            var result = SharpCompiler.Compile(example.CSharp);
            Assert.True(result.Ok, $"{example.Name}: {result.Error?.Message}");
            var machine = new Machine(Sharp16.Machine(), result.Assembly, registry);
            Assert.NotNull(machine);
        }
        Assert.True(Sharp16.Examples.Single(e => e.Name == "Sketch with the arrow keys").Needs.Keypad);
    }

    [Fact]
    public void AProgramWithAUsingDirectiveAndMainOutsideProgramStillCompiles()
    {
        var result = SharpCompiler.Compile("using System;\n\nclass Old\n{\n    static void Main() { Console.Write(1); }\n}\n");
        Assert.True(result.Ok);
        Assert.Contains(result.Lines, l => l.Text == "Main:   ENTER  0");
    }

    [Fact]
    public void EachClassHasItsOwnStaticMembersAndProgramMainRuns()
    {
        var display = Run("""
            static class Program
            {
                const int Base = 1;
                public static int count = Base * 5;

                public static void Main()
                {
                    Print();
                    Helpers.Print();
                    Console.Write(Base + Helpers.Base);
                    Console.Write(count);
                    Console.Write(Helpers.count);
                }

                static void Print() { Console.Write("P"); }
            }

            static class Helpers
            {
                public const int Base = 10;
                public static int count = Base - 3;

                public static void Print()
                {
                    Console.Write("H");
                    Write();
                }

                static void Write() { Console.Write(Base); }
            }

            class Other
            {
                static void Main() { Console.Write("wrong"); }
            }
            """);
        Assert.Equal("PH101157", display);
    }

    [Fact]
    public void SeveralMainMethodsWithoutOneInProgramFailToCompile()
    {
        var result = SharpCompiler.Compile("class A\n{\n    static void Main() { }\n}\n\nclass B\n{\n    static void Main() { }\n}\n");
        Assert.False(result.Ok);
        Assert.Equal((8, 17), (result.Error.Line, result.Error.Col));
        Assert.Contains("none in static class Program", result.Error.Message);
    }

    [Fact]
    public void AStaticMethodOfAnotherClassNeedsItsClassName()
    {
        var result = SharpCompiler.Compile("static class Program\n{\n    public static void Main() { Print(); }\n}\n\nstatic class Helpers\n{\n    public static void Print() { }\n}\n");
        Assert.False(result.Ok);
        Assert.Equal("there is no method 'Print'", result.Error.Message);
    }

    private static string Run(string csharp)
    {
        var result = SharpCompiler.Compile(csharp);
        Assert.True(result.Ok, result.Error?.Message);
        var machine = new Machine(Sharp16.Machine(), result.Assembly, DeviceRegistry.CreateDefault());
        for (int ticks = 0; !machine.IsHalted && ticks < 2_000_000; ticks++) machine.SingleStep();
        Assert.True(machine.IsHalted);
        return machine.Devices.OfType<CharacterDisplay>().Single().Line(0).TrimEnd();
    }

    [Fact]
    public void SymbolsListClassesMethodsAndFields()
    {
        var symbols = SharpCompiler.Symbols("""
            class Point
            {
                int x;
                static int count = 0;
                public Point(int start) { x = start; }
                public int Twice(int by) => x * by;
                static void Main() { }
            }
            """);
        var point = Assert.Single(symbols.Classes);
        Assert.Equal("Point", point.Name);
        Assert.Equal(new[] { "x" }, point.Fields.Select(f => f.Name));
        Assert.False(point.Fields[0].IsStatic);
        Assert.Equal("Twice", Assert.Single(point.Methods).Name);
        Assert.Equal(new SharpParameterSymbol("int", "by"), point.Methods[0].Parameters.Single());
        Assert.Equal("int", point.Methods[0].ReturnType);
        Assert.Equal("Point", point.Constructor.Name);
        Assert.Equal("start", point.Constructor.Parameters.Single().Name);
        var main = Assert.Single(symbols.Methods);
        Assert.Equal(("Main", "Point", true), (main.Name, main.Owner, main.IsStatic));
        var count = Assert.Single(symbols.Fields);
        Assert.Equal(("count", "Point", true, "int"), (count.Name, count.Owner, count.IsStatic, count.Type));
        Assert.Null(SharpCompiler.Symbols("class {"));
    }
}
