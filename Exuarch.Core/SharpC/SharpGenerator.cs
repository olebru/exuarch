using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed class SharpOutLine
    {
        public string Text { get; set; }
        public int Line { get; set; }
        public string Kind { get; init; }
        public string Mnemonic { get; set; }
        public string Operand { get; set; }
        public string Name { get; init; }
        public string Label { get; init; }
    }

    internal sealed class SharpSymbol
    {
        public string Kind { get; init; }
        public int Offset { get; init; }
        public int Value { get; init; }
        public string Type { get; init; }
        public string Label { get; init; }
        public SharpField Field { get; init; }
        public string Cls { get; init; }
        public string Name { get; init; }
        public SharpMethod Method { get; init; }

        public static SharpSymbol Of(SharpMethod method) => method == null ? null : new SharpSymbol { Kind = "method", Method = method, Name = method.Name };
    }

    internal sealed class SharpClassInfo
    {
        public string Name { get; init; }
        public Dictionary<string, SharpSymbol> Fields { get; } = new Dictionary<string, SharpSymbol>();
        public SharpOrderedMap<SharpMethod> Methods { get; } = new SharpOrderedMap<SharpMethod>();
        public List<int> RefOffsets { get; } = new List<int>();
        public SharpMethod Ctor { get; set; }
        public int Size { get; set; }
    }

    internal sealed class SharpFunctionContext
    {
        public string Name { get; init; }
        public string Ret { get; init; }
        public int Slots { get; set; }
        public string RetLabel { get; init; }
        public List<(string Brk, string Cont)> Loops { get; } = new List<(string, string)>();
        public bool TopLevel { get; init; }
        public string Cls { get; init; }
        public string Owner { get; init; }
        public string LocalNames { get; set; }
    }

    internal sealed class SharpScope
    {
        public Dictionary<string, SharpSymbol> Vars { get; } = new Dictionary<string, SharpSymbol>();
        public SharpScope Parent { get; init; }
    }

    internal sealed class SharpOrderedMap<T>
    {
        private readonly Dictionary<string, T> values = new Dictionary<string, T>();
        private readonly List<string> keys = new List<string>();

        public bool Has(string key) => key != null && values.ContainsKey(key);
        public T Get(string key) => key != null && values.TryGetValue(key, out var v) ? v : default;
        public IEnumerable<T> Values => keys.Select(k => values[k]).ToList();
        public int Count => keys.Count;
        public IEnumerable<string> KeyList => keys.ToList();

        public void Set(string key, T value)
        {
            if (!values.ContainsKey(key)) keys.Add(key);
            values[key] = value;
        }
    }

    internal sealed partial class SharpGenerator
    {
        private readonly SharpUnit unit;
        private string[] src;
        private readonly List<SharpOutLine> output = new List<SharpOutLine>();
        private int labelCount;
        private readonly List<string> runtime = new List<string>();
        private readonly SharpOrderedMap<string> strings = new SharpOrderedMap<string>();
        private readonly SharpOrderedMap<SharpMethod> functions = new SharpOrderedMap<SharpMethod>();
        private readonly SharpOrderedMap<SharpSymbol> globals = new SharpOrderedMap<SharpSymbol>();
        private readonly SharpOrderedMap<SharpClassInfo> classes = new SharpOrderedMap<SharpClassInfo>();
        private readonly List<string> instantiated = new List<string>();
        private int curLine;
        private int lastCommentLine;
        private SharpFunctionContext fn;
        private SharpScope scope;

        public SharpGenerator(SharpUnit unit, string[] sourceLines)
        {
            this.unit = unit;
            src = sourceLines;
        }

        public bool NeedsHeap { get; private set; }
        public IReadOnlyList<string> Runtime => runtime;

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static SharpCompileException Fail(string message, SharpPos node) => SharpCompileException.At(message, node);

        private void Emit(string mnemonic, string operand = null)
        {
            var text = operand == null ? $"        {mnemonic}" : $"        {mnemonic.PadRight(6)} {operand}";
            output.Add(new SharpOutLine { Text = text, Line = curLine, Kind = "ins", Mnemonic = mnemonic, Operand = operand });
        }

        private void Label(string name) => output.Add(new SharpOutLine { Text = $"{name}:", Line = curLine, Kind = "label", Name = name });

        private void Comment(string text) => Comment(text, curLine);

        private void Comment(string text, int line) => output.Add(new SharpOutLine { Text = $"; {text}", Line = line, Kind = "comment" });

        private void Blank() => output.Add(new SharpOutLine { Text = "", Line = 0, Kind = "blank" });

        private string NewLabel(string hint) => $"__{hint}{++labelCount}";

        private string SourceLine(int line) => line >= 1 && line <= src.Length ? src[line - 1] : null;

        private void At(SharpPos node)
        {
            if (node == null || node.Line == 0 || node.Line == curLine) return;
            curLine = node.Line;
            if (node.Line == lastCommentLine || SourceLine(node.Line) == null) return;
            var text = SourceLine(node.Line).Trim();
            if (text.Length == 0 || text == "{" || text == "}") return;
            Comment($"{node.Line}: {text}", node.Line);
            lastCommentLine = node.Line;
        }

        private string StringLabel(string s)
        {
            if (!strings.Has(s)) strings.Set(s, $"__str{strings.Count}");
            return strings.Get(s);
        }

        private void Use(string name)
        {
            if (runtime.Contains(name)) return;
            runtime.Add(name);
            foreach (var d in SharpRuntime.Routines[name].Deps) Use(d);
        }

        private void PushScope() => scope = new SharpScope { Parent = scope };

        private void PopScope() => scope = scope.Parent;

        private IEnumerable<SharpScope> Scopes()
        {
            for (var s = scope; s != null; s = s.Parent) yield return s;
        }

        private SharpSymbol ScopedVariable(string name) => Scopes().Where(s => s.Vars.ContainsKey(name)).Select(s => s.Vars[name]).FirstOrDefault();

        private bool InScope(string name) => Scopes().Any(s => s.Vars.ContainsKey(name));

        private int NewSlot() => fn.Slots++;
    }
}
