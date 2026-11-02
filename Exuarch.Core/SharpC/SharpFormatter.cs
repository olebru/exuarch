using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    public static class SharpFormatter
    {
        private const string Indent = "    ";
        private static readonly Regex Control = new Regex(@"^(\}\s*)?(if|else|for|foreach|while|do)\b");
        private static readonly Regex CaseLabel = new Regex(@"^(case\b.*|default\s*):");
        private static readonly Regex Switch = new Regex(@"^switch\b");

        private sealed class Block
        {
            public int Base { get; init; }
            public bool IsSwitch { get; init; }
            public bool InCase { get; set; }
        }

        private enum Carry { Code, BlockComment, Verbatim }

        private sealed class State
        {
            public Stack<Block> Blocks { get; } = new Stack<Block>();
            public Carry Inside { get; set; }
            public int PendingControls { get; set; }
            public int Parens { get; set; }
            public bool PendingSwitch { get; set; }
        }

        private sealed class ScannedLine
        {
            public StringBuilder Code { get; } = new StringBuilder();
            public List<char> Braces { get; } = new List<char>();
            public int ParenChange { get; set; }
        }

        public static string Format(string source)
        {
            var state = new State();
            return string.Join("\n", Lines(source).Select(line => FormatLine(line, state)));
        }

        public static string FormatLine(string source, int line)
        {
            var lines = Format(source).Split('\n');
            return line >= 1 && line <= lines.Length ? lines[line - 1] : "";
        }

        private static IEnumerable<string> Lines(string source) => (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static string FormatLine(string line, State state)
        {
            bool continues = state.Inside != Carry.Code;
            var trimmed = line.Trim();
            var scanned = Scan(line, state);
            if (continues) return state.Inside == Carry.Verbatim ? line : line.TrimEnd();
            if (trimmed.Length == 0) return "";
            var code = scanned.Code.ToString().Trim();
            int level = LevelOf(code, state);
            Apply(scanned, code, level, state);
            return string.Concat(Enumerable.Repeat(Indent, Math.Max(0, level))) + trimmed;
        }

        private static int LevelOf(string code, State state)
        {
            if (code.StartsWith("}")) return ClosingLevel(state);
            int inner = InnerLevel(state, CaseLabel.IsMatch(code));
            if (code.StartsWith("{")) return inner + Math.Max(0, state.PendingControls - 1);
            int continuation = state.PendingControls == 0 && state.Parens > 0 ? 1 : 0;
            return inner + state.PendingControls + continuation;
        }

        private static int ClosingLevel(State state) => state.Blocks.Count == 0 ? 0 : state.Blocks.Peek().Base;

        private static int InnerLevel(State state, bool isCaseLabel)
        {
            if (state.Blocks.Count == 0) return 0;
            var block = state.Blocks.Peek();
            if (!block.IsSwitch) return block.Base + 1;
            if (isCaseLabel)
            {
                block.InCase = true;
                return block.Base + 1;
            }
            return block.Base + (block.InCase ? 2 : 1);
        }

        private static void Apply(ScannedLine scanned, string code, int level, State state)
        {
            bool switchLine = state.PendingSwitch || Switch.IsMatch(code);
            foreach (var brace in scanned.Braces) ApplyBrace(brace, level, switchLine, state);
            state.Parens = Math.Max(0, state.Parens + scanned.ParenChange);
            state.PendingSwitch = Switch.IsMatch(code) && !scanned.Braces.Contains('{');
            state.PendingControls = NextPendingControls(code, scanned, state.PendingControls);
        }

        private static void ApplyBrace(char brace, int level, bool switchLine, State state)
        {
            if (brace == '{') state.Blocks.Push(new Block { Base = level, IsSwitch = switchLine });
            else if (state.Blocks.Count > 0) state.Blocks.Pop();
        }

        private static int NextPendingControls(string code, ScannedLine scanned, int pending)
        {
            if (scanned.Braces.Count > 0) return IsOpenControl(code) ? 1 : 0;
            if (IsOpenControl(code)) return pending + 1;
            return code.EndsWith(";") ? 0 : pending;
        }

        private static bool IsOpenControl(string code)
        {
            if (!Control.IsMatch(code)) return false;
            return !(code.EndsWith(";") || code.EndsWith("{") || code.EndsWith("}"));
        }

        private static ScannedLine Scan(string line, State state)
        {
            var scanned = new ScannedLine();
            int i = 0;
            while (i < line.Length) i = state.Inside switch
            {
                Carry.BlockComment => SkipBlockComment(line, i, state),
                Carry.Verbatim => SkipVerbatim(line, i, state),
                _ => ScanCode(line, i, state, scanned),
            };
            return scanned;
        }

        private static int ScanCode(string line, int i, State state, ScannedLine scanned)
        {
            char c = line[i];
            if (c == '/' && At(line, i + 1) == '/') return line.Length;
            if (c == '/' && At(line, i + 1) == '*') { state.Inside = Carry.BlockComment; return i + 2; }
            if (c == '"' || c == '\'') return SkipQuoted(line, i + 1, c, scanned);
            if (c == '$' || c == '@') return StringPrefix(line, i, state, scanned);
            Record(c, scanned);
            return i + 1;
        }

        private static int StringPrefix(string line, int i, State state, ScannedLine scanned)
        {
            int start = i;
            bool verbatim = false;
            while (i < line.Length && (line[i] == '$' || line[i] == '@')) verbatim |= line[i++] == '@';
            if (At(line, i) != '"') { scanned.Code.Append(line, start, i - start); return i; }
            if (!verbatim) return SkipQuoted(line, i + 1, '"', scanned);
            scanned.Code.Append("\"\"");
            state.Inside = Carry.Verbatim;
            return i + 1;
        }

        private static void Record(char c, ScannedLine scanned)
        {
            scanned.Code.Append(c);
            if (c == '{' || c == '}') scanned.Braces.Add(c);
            else if (c == '(') scanned.ParenChange++;
            else if (c == ')') scanned.ParenChange--;
        }

        private static int SkipQuoted(string line, int i, char quote, ScannedLine scanned)
        {
            scanned.Code.Append(quote).Append(quote);
            while (i < line.Length && line[i] != quote) i += line[i] == '\\' ? 2 : 1;
            return Math.Min(line.Length, i + 1);
        }

        private static int SkipBlockComment(string line, int i, State state)
        {
            int end = line.IndexOf("*/", i, StringComparison.Ordinal);
            if (end < 0) return line.Length;
            state.Inside = Carry.Code;
            return end + 2;
        }

        private static int SkipVerbatim(string line, int i, State state)
        {
            while (i < line.Length)
            {
                if (line[i] != '"') { i++; continue; }
                if (At(line, i + 1) == '"') { i += 2; continue; }
                state.Inside = Carry.Code;
                return i + 1;
            }
            return i;
        }

        private static char At(string line, int i) => i < line.Length ? line[i] : '\0';
    }
}
