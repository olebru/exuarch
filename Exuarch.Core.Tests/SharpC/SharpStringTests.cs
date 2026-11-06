using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Exuarch.Core;
using Exuarch.Core.SharpC;

namespace Exuarch.Core.Tests.SharpC;

public class SharpStringTests
{
    private static string Run(string csharp)
    {
        var result = SharpCompiler.Compile(csharp);
        Assert.True(result.Ok, result.Error?.Message);
        var machine = new Machine(Sharp16.Machine(), result.Assembly, DeviceRegistry.CreateDefault());
        for (int ticks = 0; !machine.IsHalted && ticks < 20_000_000; ticks++) machine.SingleStep();
        Assert.True(machine.IsHalted);
        return machine.Devices.OfType<CharacterDisplay>().Single().Line(0).TrimEnd();
    }

    private static string CompileError(string csharp)
    {
        var result = SharpCompiler.Compile(csharp);
        Assert.False(result.Ok);
        return result.Error.Message;
    }

    [Fact]
    public void PlusJoinsStringsCharsNumbersAndBools()
    {
        var display = Run("""
            static void Show(int n, char c, bool b)
            {
                string s = "n" + n;
                string t = s + c + b + '|' + (n + 1);
                Console.Write(t);
            }
            Show(42, 'c', true);
            """);
        Assert.Equal("n42cTrue|43", display);
    }

    [Fact]
    public void NumbersBecomeTextAcrossTheWholeRange()
    {
        var display = Run("""
            static string Text(int n) => "" + n;
            Console.Write(Text(0) + "," + Text(7) + "," + Text(-5) + "," + Text(32767) + "," + Text(-32768) + "," + Text(-10));
            """);
        Assert.Equal("0,7,-5,32767,-32768,-10", display);
    }

    [Fact]
    public void AnInterpolatedStringCanBeKept()
    {
        var display = Run("""
            static string Describe(int x, bool on) => $"x is {x}, on is {on}";
            string kept = Describe(-3, false);
            Console.Write(kept);
            """);
        Assert.Equal("x is -3, on is False", display);
    }

    [Fact]
    public void PlusEqualsAppendsToLocalsFieldsAndElements()
    {
        var display = Run("""
            class Box { public string text = "b"; }

            static class Program
            {
                static string shared = "s";

                static void Main()
                {
                    string local = "l";
                    local += 1;
                    shared += 'h';
                    var box = new Box();
                    box.text += "ox";
                    string[] items = { "i" };
                    items[0] += true;
                    Console.Write(local + shared + box.text + items[0]);
                }
            }
            """);
        Assert.Equal("l1shboxiTrue", display);
    }

    [Fact]
    public void NewStringAndToCharArrayConvertBothWays()
    {
        var display = Run("""
            string word = "cat";
            char[] letters = word.ToCharArray();
            letters[0] = 'b';
            string changed = new string(letters);
            Console.Write(changed + word + new string('-', 3) + letters.Length);
            """);
        Assert.Equal("batcat---3", display);
    }

    [Fact]
    public void StringConcatJoinsItsArguments()
    {
        var display = Run("""
            static void Show(int n) { Console.Write(string.Concat(n, "a", 'b') + String.Concat("x", "y")); }
            Show(5);
            """);
        Assert.Equal("5abxy", display);
    }

    [Fact]
    public void EqualsComparesTheCharacters()
    {
        var display = Run("""
            static void Show(string a, string b)
            {
                string joined = a + b;
                string none = null;
                Console.Write(joined == "abc");
                Console.Write(joined != "abc");
                Console.Write(joined == "abd");
                Console.Write(joined == "ab");
                Console.Write(none == null);
                Console.Write(none == joined);
                if (a + "x" == "ax") Console.Write("!");
            }
            Show("a", "bc");
            """);
        Assert.Equal("TrueFalseFalseFalseTrueFalse!", display);
    }

    [Fact]
    public void StringsKeptInFieldsAndArraysSurviveACollection()
    {
        var display = Run("""
            class Holder { public string text; }

            static class Program
            {
                static string kept;

                static void Main()
                {
                    kept = "st" + Number(1);
                    var holder = new Holder();
                    holder.text = "fi" + Number(2);
                    string[] items = new string[2];
                    items[0] = "ar" + Number(3);
                    items[1] = "literal";
                    for (int i = 0; i < 50; i++) { string waste = "w" + i; }
                    GC.Collect();
                    for (int i = 0; i < 50; i++) { string waste = "w" + i; }
                    Console.Write(kept + holder.text + items[0] + items[1]);
                }

                static int Number(int n) => n;
            }
            """);
        Assert.Equal("st1fi2ar3literal", display);
    }

    [Fact]
    public void AFieldThatPointsAtALiteralLeavesTheDataUnchangedThroughACollection()
    {
        var display = Run("""
            class Holder { public string first; public string second; }

            static class Program
            {
                static void Main()
                {
                    var holder = new Holder();
                    holder.first = "one";
                    holder.second = "two";
                    GC.Collect();
                    GC.Collect();
                    Console.Write(holder.first + holder.second);
                }
            }
            """);
        Assert.Equal("onetwo", display);
    }

    [Fact]
    public void ManyShortLivedStringsAreCollected()
    {
        var display = Run("""
            string last = "";
            for (int i = 0; i < 40; i++) last = new string('x', 1000) + i;
            Console.Write(last.Length + " " + last[1000] + last[1001]);
            """);
        Assert.Equal("1002 39", display);
    }

    [Fact]
    public void TheRuntimeStringsCaseWritesWhatItBuilt()
    {
        var display = Run(File.ReadAllText(Path.Combine(CasesFolder(), "runtime-strings.csharp")));
        Assert.Equal("Tab7--True", display);
    }

    private static string CasesFolder([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path), "Cases");

    [Fact]
    public void AConstantJoinIsOneLiteral()
    {
        var result = SharpCompiler.Compile("""
            string s = "a" + 1 + 'b' + true;
            Console.Write(s);
            """);
        Assert.True(result.Ok);
        Assert.Contains(result.Lines, l => l.Text.Contains(".STRING \"a1bTrue\""));
        Assert.DoesNotContain("__New", result.Runtime);
    }

    [Theory]
    [InlineData("string a = \"x\"; Console.Write(a < \"y\");", "'<' does not work on strings: they can only be compared with == and !=")]
    [InlineData("string a = \"x\"; Console.Write(a == 1);", "can not compare a string with an int")]
    [InlineData("string a = \"x\"; a -= \"y\";", "'-' needs numbers, but the left side is string")]
    [InlineData("int n = 1; n += \"y\";", "can not use a string where an int is needed")]
    [InlineData("string a = \"x\"; var b = a.Substring(1);", "'Substring' is not available on a string. SharpC strings have Length, s[i] and ToCharArray()")]
    [InlineData("string a = new string(5);", "new string(…) needs a char[], not an int")]
    [InlineData("string a = new string(\"x\", 2);", "new string(c, count) needs a char first, not a string")]
    public void UnsupportedStringOperationsFailToCompile(string body, string message)
    {
        Assert.Equal(message, CompileError(body));
    }
}
