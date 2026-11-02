using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    public sealed record SharpLibraryClass(string Name, string Documentation);

    public sealed record SharpLibraryMember(string Class, string Name, string ReturnType, IReadOnlyList<SharpParameterSymbol> Parameters, string Documentation, int? Value = null)
    {
        public bool IsConstant => Value.HasValue;
        public string Signature => IsConstant
            ? $"const int {Class}.{Name} = {Value}"
            : $"{ReturnType} {Class}.{Name}({string.Join(", ", Parameters.Select(p => $"{p.Type} {p.Name}"))})";
    }

    public static class SharpLibrary
    {
        private const string KeysRead = "Reads the keypad: one bit for each key held down, to test with `Keys.Up`, `Keys.Down`, `Keys.Left`, `Keys.Right` and `Keys.Space`. The program needs the keypad in the Run view.";
        private const string WriteFormat = "With more than one argument, the first is a format string such as `\"{0} and {1}\"`. An interpolated string `$\"…\"` and joining strings with `+` work only here.";

        public static readonly IReadOnlyList<SharpLibraryClass> Classes = new[]
        {
            new SharpLibraryClass("Console", "The text display: text out, one character at a time."),
            new SharpLibraryClass("Screen", "The screen: 16-bit RGB565 pixels, written at a position that moves on by itself."),
            new SharpLibraryClass("Keys", "The keypad: the arrow keys and space."),
            new SharpLibraryClass("Math", "Whole-number helpers."),
            new SharpLibraryClass("GC", "The garbage collector of the heap that `new` takes objects and arrays from."),
            new SharpLibraryClass("Machine", "The SHARP-16 machine itself."),
            new SharpLibraryClass("Environment", "The program's surroundings."),
        };

        public static readonly IReadOnlyList<SharpLibraryMember> Members = new[]
        {
            Method("Console", "Write", "void", "Writes a number, a character, a bool or a string to the text display. " + WriteFormat, ("object", "value")),
            Method("Console", "Write", "void", "Writes the format string with each `{n}` replaced by argument n.", ("string", "format"), ("params object[]", "args")),
            Method("Console", "WriteLine", "void", "Writes a number, a character, a bool or a string to the text display, then a line break. Without an argument, only the line break. " + WriteFormat, ("object", "value")),
            Method("Console", "WriteLine", "void", "Writes the format string with each `{n}` replaced by argument n, then a line break.", ("string", "format"), ("params object[]", "args")),
            Method("Console", "Clear", "void", "Clears the text display."),
            Method("Screen", "Clear", "void", "Clears the screen."),
            Method("Screen", "Plot", "void", "Sets the pixel at x, y to the colour.", ("int", "x"), ("int", "y"), ("int", "colour")),
            Method("Screen", "MoveTo", "void", "Moves the write position to x, y, for `Screen.Put`.", ("int", "x"), ("int", "y")),
            Method("Screen", "Put", "void", "Writes the colour at the write position, which then moves on to the next pixel.", ("int", "colour")),
            Method("Screen", "Rgb", "int", "Packs red, green and blue, 0 to 255 each, into one 16-bit RGB565 colour. With constant arguments the colour is worked out when compiling.", ("int", "r"), ("int", "g"), ("int", "b")),
            Method("Keys", "Read", "int", KeysRead),
            Constant("Keys", "Up", 1, "The bit for the up arrow in `Keys.Read()`."),
            Constant("Keys", "Down", 2, "The bit for the down arrow in `Keys.Read()`."),
            Constant("Keys", "Left", 4, "The bit for the left arrow in `Keys.Read()`."),
            Constant("Keys", "Right", 8, "The bit for the right arrow in `Keys.Read()`."),
            Constant("Keys", "Space", 16, "The bit for the space bar in `Keys.Read()`."),
            Method("Math", "Abs", "int", "The value without its sign.", ("int", "value")),
            Method("Math", "Min", "int", "The smaller of two numbers.", ("int", "a"), ("int", "b")),
            Method("Math", "Max", "int", "The larger of two numbers.", ("int", "a"), ("int", "b")),
            Method("GC", "Collect", "void", "Frees the objects and arrays that nothing refers to any more, so `new` can use their memory again."),
            Method("Machine", "Halt", "void", "Stops the clock: the program ends here."),
            Method("Environment", "Exit", "void", "Stops the clock: the program ends here. The exit code is not used.", ("int", "exitCode")),
        };

        public static readonly IReadOnlyDictionary<string, string> TypeDocumentation = new Dictionary<string, string>
        {
            ["int"] = "A 16-bit signed whole number, from -32768 to 32767.",
            ["short"] = "The same as `int` on SHARP-16: 16 bits, signed.",
            ["bool"] = "`true` or `false`.",
            ["char"] = "One character.",
            ["string"] = "Text. `Length` and `s[i]` read it; it can not be changed.",
            ["void"] = "No value: a method that returns nothing.",
            ["var"] = "The type of the value it is set to.",
        };

        public static SharpLibraryClass Class(string name) => Classes.FirstOrDefault(c => c.Name == name);

        public static IEnumerable<SharpLibraryMember> MembersOf(string className) => Members.Where(m => m.Class == className);

        private static SharpLibraryMember Method(string cls, string name, string returns, string documentation, params (string Type, string Name)[] parameters)
        {
            return new SharpLibraryMember(cls, name, returns, parameters.Select(p => new SharpParameterSymbol(p.Type, p.Name)).ToList(), documentation);
        }

        private static SharpLibraryMember Constant(string cls, string name, int value, string documentation)
        {
            return new SharpLibraryMember(cls, name, "int", new List<SharpParameterSymbol>(), documentation, value);
        }
    }
}
