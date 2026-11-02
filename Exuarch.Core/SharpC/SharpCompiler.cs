using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    public sealed class SharpCompilation
    {
        public bool Ok { get; init; }
        public string Assembly { get; init; }
        public IReadOnlyList<SharpAsmLine> Lines { get; init; }
        public IReadOnlyList<string> Runtime { get; init; }
        public SharpCompileError Error { get; init; }
    }

    public sealed record SharpAsmLine(string Text, int SourceLine, string Kind, string Label);

    public sealed record SharpCompileError(string Message, int Line, int Col);

    public sealed record SharpProgramSymbols(IReadOnlyList<SharpClassSymbol> Classes, IReadOnlyList<SharpMethodSymbol> Methods, IReadOnlyList<SharpFieldSymbol> Fields);

    public sealed record SharpClassSymbol(string Name, int Line, IReadOnlyList<SharpFieldSymbol> Fields, IReadOnlyList<SharpMethodSymbol> Methods, SharpMethodSymbol Constructor);

    public sealed record SharpMethodSymbol(string Name, string Owner, string ReturnType, IReadOnlyList<SharpParameterSymbol> Parameters, bool IsStatic, int Line);

    public sealed record SharpParameterSymbol(string Type, string Name);

    public sealed record SharpFieldSymbol(string Name, string Owner, string Type, bool IsConst, bool IsStatic, int Line);

    public static class SharpCompiler
    {
        private const int MergedLabelWidth = 8;

        public static SharpCompilation Compile(string source)
        {
            var sourceLines = Regex.Split(source ?? "", "\r\n|\r|\n");
            try
            {
                var unit = new SharpParser(SharpLexer.Lex(source ?? "")).Program();
                var generator = new SharpGenerator(unit, sourceLines);
                var merged = MergeLabels(generator.Generate());
                var runtime = generator.Runtime.Concat(generator.NeedsHeap ? new[] { "__New", "__Collect" } : new string[0]).ToList();
                return new SharpCompilation
                {
                    Ok = true,
                    Assembly = string.Join("\n", merged.Select(x => x.Text)) + "\n",
                    Lines = merged,
                    Runtime = runtime,
                };
            }
            catch (SharpCompileException e)
            {
                return new SharpCompilation { Ok = false, Error = new SharpCompileError(e.Message, e.Line, e.Col), Lines = new List<SharpAsmLine>(), Runtime = new List<string>() };
            }
        }

        private static List<SharpAsmLine> MergeLabels(List<SharpOutLine> lines)
        {
            var merged = new List<SharpAsmLine>();
            for (int i = 0; i < lines.Count; i++)
            {
                var x = lines[i];
                var next = i + 1 < lines.Count ? lines[i + 1] : null;
                if (FoldsInto(x, next))
                {
                    var text = x.Text.PadRight(MergedLabelWidth) + next.Text.Substring(System.Math.Min(MergedLabelWidth, next.Text.Length));
                    merged.Add(new SharpAsmLine(text, next.Line != 0 ? next.Line : x.Line, next.Kind, x.Name));
                    i++;
                }
                else merged.Add(new SharpAsmLine(x.Text, x.Line, x.Kind, x.Kind == "label" ? x.Name : null));
            }
            return merged;
        }

        private static bool FoldsInto(SharpOutLine label, SharpOutLine next)
        {
            return label.Kind == "label" && next != null && next.Kind == "ins" && label.Text.Length < MergedLabelWidth;
        }

        public static SharpProgramSymbols Symbols(string source)
        {
            try
            {
                var unit = new SharpParser(SharpLexer.Lex(source ?? "")).Program();
                return new SharpProgramSymbols(unit.Classes.Select(ClassSymbol).ToList(), unit.Methods.Select(m => MethodSymbol(m, true)).ToList(), unit.Fields.Select(f => FieldSymbol(f, true)).ToList());
            }
            catch (SharpCompileException)
            {
                return null;
            }
        }

        private static SharpClassSymbol ClassSymbol(SharpClass c)
        {
            var constructor = c.Ctor == null ? null : MethodSymbol(c.Ctor, false) with { Name = c.Name, Owner = c.Name };
            return new SharpClassSymbol(c.Name, c.Line, c.Fields.Select(f => FieldSymbol(f, false)).ToList(), c.Methods.Select(m => MethodSymbol(m, false)).ToList(), constructor);
        }

        private static SharpMethodSymbol MethodSymbol(SharpMethod m, bool isStatic)
        {
            var parameters = m.Params.Select(p => new SharpParameterSymbol(p.Type.Name, p.Name)).ToList();
            return new SharpMethodSymbol(m.Name, m.Owner, m.Ret.Name, parameters, isStatic, m.Line);
        }

        private static SharpFieldSymbol FieldSymbol(SharpField f, bool isStatic) => new SharpFieldSymbol(f.Name, f.Owner, f.Type.Name, f.IsConst, isStatic, f.Line);
    }
}
