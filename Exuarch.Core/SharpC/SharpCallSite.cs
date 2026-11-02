using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    public sealed class SharpCallSite
    {
        private static readonly Regex Callee = new Regex(@"(new\s+)?(?:([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*$");

        public string Qualifier { get; private init; }
        public string Name { get; private init; }
        public bool IsNew { get; private init; }
        public int Argument { get; private init; }

        public static SharpCallSite Find(string textBeforeCursor)
        {
            var text = textBeforeCursor ?? "";
            var opens = OpenBrackets(text);
            if (opens.Count == 0 || text[opens.Peek()] != '(') return null;
            int open = opens.Peek();
            var callee = Callee.Match(text.Substring(0, open));
            if (!callee.Success) return null;
            return new SharpCallSite
            {
                IsNew = callee.Groups[1].Success,
                Qualifier = callee.Groups[2].Success ? callee.Groups[2].Value : null,
                Name = callee.Groups[3].Value,
                Argument = TopLevelCommas(text, open + 1),
            };
        }

        private static Stack<int> OpenBrackets(string text)
        {
            var opens = new Stack<int>();
            for (int i = 0; i < text.Length; i++)
            {
                i = SkipLiteral(text, i);
                if (i >= text.Length) break;
                int change = DepthChange(text[i]);
                if (change > 0) opens.Push(i);
                else if (change < 0 && opens.Count > 0) opens.Pop();
            }
            return opens;
        }

        private static int TopLevelCommas(string text, int start)
        {
            int depth = 0, commas = 0;
            for (int i = start; i < text.Length; i++)
            {
                i = SkipLiteral(text, i);
                if (i >= text.Length) break;
                int change = DepthChange(text[i]);
                depth += change;
                if (change == 0 && depth == 0 && text[i] == ',') commas++;
            }
            return commas;
        }

        private static int DepthChange(char c) => "([{".IndexOf(c) >= 0 ? 1 : ")]}".IndexOf(c) >= 0 ? -1 : 0;

        private static int SkipLiteral(string text, int i)
        {
            if (StartsWith(text, i, "//")) return SkipTo(text, i, "\n");
            if (StartsWith(text, i, "/*")) return SkipTo(text, i + 2, "*/") + 1;
            char c = text[i];
            return c == '"' || c == '\'' ? SkipQuoted(text, i, c) : i;
        }

        private static bool StartsWith(string text, int i, string token) => string.CompareOrdinal(text, i, token, 0, token.Length) == 0;

        private static int SkipQuoted(string text, int i, char quote)
        {
            int j = i + 1;
            while (j < text.Length && text[j] != quote && text[j] != '\n') j += text[j] == '\\' ? 2 : 1;
            return j < text.Length ? j + 1 : text.Length;
        }

        private static int SkipTo(string text, int from, string end)
        {
            int at = text.IndexOf(end, from, System.StringComparison.Ordinal);
            return at < 0 ? text.Length : at;
        }
    }
}
