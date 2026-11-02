using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    public enum SharpCompletionKind { Keyword, Type, Class, Method, Field, Constant, Variable, Snippet, Property }

    public sealed record SharpCompletion(string Label, SharpCompletionKind Kind, string Detail, string Documentation, string InsertText, bool IsSnippet = false);

    public sealed record SharpSignature(string Label, IReadOnlyList<string> Parameters, string Documentation);

    public sealed record SharpSignatureHelp(IReadOnlyList<SharpSignature> Signatures, int ActiveParameter);

    public static class SharpLanguageService
    {
        private static readonly Regex MemberAccess = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*[A-Za-z0-9_]*$");
        private static readonly Regex Identifier = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");

        public static readonly IReadOnlyList<string> Keywords = new[]
        {
            "class", "static", "public", "private", "internal", "protected", "readonly", "const", "partial", "sealed", "namespace", "using",
            "if", "else", "while", "do", "for", "foreach", "in", "switch", "case", "default", "break", "continue", "return",
            "true", "false", "new", "null", "this",
        };

        public static readonly IReadOnlyList<string> Types = new[] { "int", "bool", "char", "string", "void", "var", "short" };

        private static readonly SharpCompletion[] Snippets =
        {
            Snippet("for", "for loop", "for (int ${1:i} = 0; ${1:i} < ${2:count}; ${1:i}++)\n{\n\t$0\n}"),
            Snippet("foreach", "foreach loop", "foreach (${1:int} ${2:item} in ${3:array})\n{\n\t$0\n}"),
            Snippet("if", "if statement", "if (${1:condition})\n{\n\t$0\n}"),
            Snippet("while", "while loop", "while (${1:condition})\n{\n\t$0\n}"),
            Snippet("switch", "switch statement", "switch (${1:value})\n{\n\tcase ${2:0}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}"),
            Snippet("class", "class", "class ${1:Name}\n{\n\t$0\n}"),
            Snippet("main", "static void Main()", "static void Main()\n{\n\t$0\n}"),
            Snippet("cw", "Console.WriteLine", "Console.WriteLine($0);"),
        };

        public static IReadOnlyList<SharpCompletion> Complete(string source, int line, int column, SharpProgramSymbols symbols)
        {
            var before = TextBefore(source, line, column);
            if (InCommentOrString(before)) return Array.Empty<SharpCompletion>();
            var access = MemberAccess.Match(before);
            return access.Success ? Members(access.Groups[1].Value, symbols).ToList() : Everything(source, symbols).ToList();
        }

        public static string Hover(string source, int line, int column, SharpProgramSymbols symbols)
        {
            var (qualifier, word) = WordAt(source, line, column);
            if (word == null) return null;
            if (qualifier != null && SharpLibrary.Class(qualifier) != null) return LibraryHover(qualifier, word);
            if (SharpLibrary.Class(word) is { } cls) return Code($"static class {cls.Name}") + cls.Documentation;
            if (SharpLibrary.TypeDocumentation.TryGetValue(word, out var doc)) return Code(word) + doc;
            return SymbolHover(word, symbols);
        }

        public static SharpSignatureHelp SignatureHelp(string source, int line, int column, SharpProgramSymbols symbols)
        {
            var call = SharpCallSite.Find(TextUpTo(source, line, column));
            if (call == null) return null;
            var signatures = SignaturesFor(call, symbols).ToList();
            return signatures.Count == 0 ? null : new SharpSignatureHelp(signatures, call.Argument);
        }

        private static IEnumerable<SharpSignature> SignaturesFor(SharpCallSite call, SharpProgramSymbols symbols)
        {
            if (call.Qualifier != null && SharpLibrary.Class(call.Qualifier) != null)
                return SharpLibrary.MembersOf(call.Qualifier).Where(m => m.Name == call.Name && !m.IsConstant).Select(m => new SharpSignature(m.Signature, m.Parameters.Select(Parameter).ToList(), m.Documentation));
            if (call.IsNew) return Constructors(call.Name, symbols);
            return UserMethods(symbols).Where(m => m.Name == call.Name).Select(m => new SharpSignature(MethodSignature(m), m.Parameters.Select(Parameter).ToList(), null));
        }

        private static IEnumerable<SharpSignature> Constructors(string name, SharpProgramSymbols symbols)
        {
            var cls = symbols?.Classes.FirstOrDefault(c => c.Name == name);
            if (cls == null) return Enumerable.Empty<SharpSignature>();
            var parameters = cls.Constructor?.Parameters ?? Array.Empty<SharpParameterSymbol>();
            return new[] { new SharpSignature($"new {name}({string.Join(", ", parameters.Select(Parameter))})", parameters.Select(Parameter).ToList(), null) };
        }

        private static IEnumerable<SharpCompletion> Members(string qualifier, SharpProgramSymbols symbols)
        {
            if (SharpLibrary.Class(qualifier) != null) return LibraryMembers(qualifier);
            return IsClassName(qualifier, symbols) ? StaticMembers(qualifier, symbols) : InstanceMembers(symbols);
        }

        private static bool IsClassName(string name, SharpProgramSymbols symbols)
        {
            if (symbols == null) return false;
            return symbols.Classes.Any(c => c.Name == name) || symbols.Methods.Any(m => m.Owner == name) || symbols.Fields.Any(f => f.Owner == name);
        }

        private static IEnumerable<SharpCompletion> LibraryMembers(string qualifier)
        {
            return SharpLibrary.MembersOf(qualifier).GroupBy(m => m.Name).Select(g => g.First()).Select(m => new SharpCompletion(
                m.Name, m.IsConstant ? SharpCompletionKind.Constant : SharpCompletionKind.Method, m.Signature, m.Documentation, m.Name));
        }

        private static IEnumerable<SharpCompletion> StaticMembers(string owner, SharpProgramSymbols symbols)
        {
            var methods = symbols.Methods.Where(m => m.Owner == owner).Select(MethodCompletion);
            var fields = symbols.Fields.Where(f => f.Owner == owner).Select(FieldCompletion);
            return methods.Concat(fields);
        }

        private static IEnumerable<SharpCompletion> InstanceMembers(SharpProgramSymbols symbols)
        {
            var length = new SharpCompletion("Length", SharpCompletionKind.Property, "int Length", "The number of elements of an array, or of characters in a string.", "Length");
            if (symbols == null) return new[] { length };
            var members = symbols.Classes.SelectMany(c => c.Methods.Select(MethodCompletion).Concat(c.Fields.Select(FieldCompletion)));
            return new[] { length }.Concat(members).GroupBy(m => m.Label).Select(g => g.First());
        }

        private static IEnumerable<SharpCompletion> Everything(string source, SharpProgramSymbols symbols)
        {
            var known = new List<SharpCompletion>();
            known.AddRange(Snippets);
            known.AddRange(Types.Select(t => new SharpCompletion(t, SharpCompletionKind.Type, t, SharpLibrary.TypeDocumentation.GetValueOrDefault(t), t)));
            known.AddRange(Keywords.Select(k => new SharpCompletion(k, SharpCompletionKind.Keyword, null, null, k)));
            known.AddRange(SharpLibrary.Classes.Select(c => new SharpCompletion(c.Name, SharpCompletionKind.Class, $"static class {c.Name}", c.Documentation, c.Name)));
            known.AddRange(ProgramSymbols(symbols));
            var names = new HashSet<string>(known.Select(k => k.Label));
            var words = Identifier.Matches(source ?? "").Select(m => m.Value).Distinct().Where(w => w.Length > 1 && names.Add(w));
            return known.Concat(words.Select(w => new SharpCompletion(w, SharpCompletionKind.Variable, null, null, w)));
        }

        private static IEnumerable<SharpCompletion> ProgramSymbols(SharpProgramSymbols symbols)
        {
            if (symbols == null) return Enumerable.Empty<SharpCompletion>();
            var classes = symbols.Classes.Select(c => new SharpCompletion(c.Name, SharpCompletionKind.Class, $"class {c.Name}", null, c.Name));
            var methods = symbols.Methods.Select(MethodCompletion);
            var fields = symbols.Fields.Select(FieldCompletion);
            return classes.Concat(methods).Concat(fields).GroupBy(c => c.Label).Select(g => g.First());
        }

        private static SharpCompletion MethodCompletion(SharpMethodSymbol m) => new SharpCompletion(m.Name, SharpCompletionKind.Method, MethodSignature(m), null, m.Name);

        private static SharpCompletion FieldCompletion(SharpFieldSymbol f) => new SharpCompletion(f.Name, f.IsConst ? SharpCompletionKind.Constant : SharpCompletionKind.Field, FieldSignature(f), null, f.Name);

        private static string LibraryHover(string qualifier, string word)
        {
            var members = SharpLibrary.MembersOf(qualifier).Where(m => m.Name == word).ToList();
            if (members.Count == 0) return null;
            return string.Join("\n\n", members.Select(m => Code(m.Signature) + m.Documentation));
        }

        private static string SymbolHover(string word, SharpProgramSymbols symbols)
        {
            if (symbols == null) return null;
            if (symbols.Classes.FirstOrDefault(c => c.Name == word) is { } cls) return ClassHover(cls);
            var methods = UserMethods(symbols).Where(m => m.Name == word).Select(m => Code(MethodSignature(m))).ToList();
            if (methods.Count > 0) return string.Join("\n\n", methods);
            var field = symbols.Fields.Concat(symbols.Classes.SelectMany(c => c.Fields)).FirstOrDefault(f => f.Name == word);
            return field == null ? null : Code(FieldSignature(field));
        }

        private static string ClassHover(SharpClassSymbol cls)
        {
            var members = cls.Fields.Select(FieldSignature).Concat(cls.Methods.Select(MethodSignature)).Select(m => $"- `{m}`");
            return Code($"class {cls.Name}") + string.Join("\n", members);
        }

        private static IEnumerable<SharpMethodSymbol> UserMethods(SharpProgramSymbols symbols)
        {
            return symbols == null ? Enumerable.Empty<SharpMethodSymbol>() : symbols.Methods.Concat(symbols.Classes.SelectMany(c => c.Methods));
        }

        private static string MethodSignature(SharpMethodSymbol m)
        {
            var owner = m.Owner == null ? "" : $"{m.Owner}.";
            return $"{(m.IsStatic ? "static " : "")}{m.ReturnType} {owner}{m.Name}({string.Join(", ", m.Parameters.Select(Parameter))})";
        }

        private static string FieldSignature(SharpFieldSymbol f)
        {
            var modifier = f.IsConst ? "const " : f.IsStatic ? "static " : "";
            var owner = f.Owner == null ? "" : $"{f.Owner}.";
            return $"{modifier}{f.Type} {owner}{f.Name}";
        }

        private static string Parameter(SharpParameterSymbol p) => $"{p.Type} {p.Name}";

        private static string Code(string signature) => $"```csharp\n{signature}\n```\n\n";

        private static SharpCompletion Snippet(string label, string detail, string body) => new SharpCompletion(label, SharpCompletionKind.Snippet, detail, null, body, true);

        private static string[] SourceLines(string source) => (source ?? "").Replace("\r\n", "\n").Split('\n');

        private static string TextBefore(string source, int line, int column)
        {
            var lines = SourceLines(source);
            if (line < 1 || line > lines.Length) return "";
            var text = lines[line - 1];
            return text.Substring(0, Math.Clamp(column - 1, 0, text.Length));
        }

        private static string TextUpTo(string source, int line, int column)
        {
            var lines = SourceLines(source).Take(Math.Max(0, line)).ToArray();
            if (lines.Length == 0) return "";
            lines[^1] = TextBefore(source, line, column);
            return string.Join("\n", lines);
        }

        private static bool InCommentOrString(string before)
        {
            bool inString = false;
            for (int i = 0; i < before.Length; i++)
            {
                char c = before[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '/' && i + 1 < before.Length && before[i + 1] == '/') return true;
                if (c == '"') inString = true;
            }
            return inString;
        }

        private static (string Qualifier, string Word) WordAt(string source, int line, int column)
        {
            var lines = SourceLines(source);
            if (line < 1 || line > lines.Length) return (null, null);
            var text = lines[line - 1];
            var match = Identifier.Matches(text).FirstOrDefault(m => m.Index < column && column <= m.Index + m.Length + 1);
            if (match == null) return (null, null);
            var qualifier = Regex.Match(text.Substring(0, match.Index), @"([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*$");
            return (qualifier.Success ? qualifier.Groups[1].Value : null, match.Value);
        }
    }
}
