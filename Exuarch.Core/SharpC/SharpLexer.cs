using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    internal sealed class SharpLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "using", "namespace", "class", "struct", "static", "public", "private", "internal", "protected", "readonly", "partial", "sealed", "const",
            "void", "int", "short", "bool", "char", "string", "var", "if", "else", "while", "do", "for", "foreach", "in", "switch", "case", "default",
            "break", "continue", "return", "true", "false", "new", "null", "this", "long", "uint", "ushort", "byte", "sbyte", "float", "double", "decimal", "object", "ulong",
        };

        private static readonly string[] Operators =
        {
            ">>>=", "<<=", ">>=", ">>>", "&&", "||", "==", "!=", "<=", ">=", "<<", ">>", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "=>",
            "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "<", ">", "=", "?", ":", ";", ",", ".", "(", ")", "{", "}", "[", "]",
        };

        private static readonly Dictionary<char, int> Escapes = new Dictionary<char, int>
        {
            ['n'] = 10, ['t'] = 9, ['r'] = 13, ['0'] = 0, ['\\'] = 92, ['\''] = 39, ['"'] = 34, ['a'] = 7, ['b'] = 8, ['f'] = 12, ['v'] = 11,
        };

        private readonly string src;
        private readonly List<SharpToken> tokens = new List<SharpToken>();
        private int i;
        private int line;
        private int col;

        private SharpLexer(string src, int baseLine, int baseCol)
        {
            this.src = src;
            line = baseLine;
            col = baseCol;
        }

        public static List<SharpToken> Lex(string src, int baseLine = 1, int baseCol = 1)
        {
            var lexer = new SharpLexer(src, baseLine, baseCol);
            lexer.Run();
            return lexer.tokens;
        }

        private bool AtEnd => i >= src.Length;
        private char Current => i < src.Length ? src[i] : '\0';
        private char Ahead => i + 1 < src.Length ? src[i + 1] : '\0';

        private static bool IsLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
        private static bool IsDigit(char c) => c >= '0' && c <= '9';
        private static bool IsWordChar(char c) => IsLetter(c) || IsDigit(c);
        private static bool IsHex(char c) => IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private void Advance(int n = 1)
        {
            for (int k = 0; k < n; k++) Step();
        }

        private void Step()
        {
            if (i < src.Length && src[i] == '\n')
            {
                line++;
                col = 1;
            }
            else col++;
            i++;
        }

        private SharpCompileException Error(string message) => new SharpCompileException(message, line, col);

        private void Run()
        {
            while (i < src.Length)
            {
                if (SkipSpaceOrComment()) continue;
                var start = (line, col);
                tokens.Add(Token(start.line, start.col));
            }
            tokens.Add(new SharpToken { Kind = "eof", Text = "end of file", Line = line, Col = col });
        }

        private bool SkipSpaceOrComment()
        {
            char c = src[i];
            if (c == '\n' || c == ' ' || c == '\t' || c == '\r') { Advance(); return true; }
            if (c == '/' && Ahead == '/') { SkipToLineEnd(); return true; }
            if (c == '/' && Ahead == '*') { SkipBlockComment(); return true; }
            if (c == '#') { SkipToLineEnd(); return true; }
            return false;
        }

        private void SkipToLineEnd()
        {
            while (i < src.Length && src[i] != '\n') Advance();
        }

        private void SkipBlockComment()
        {
            Advance(2);
            while (i < src.Length && !(src[i] == '*' && Ahead == '/')) Advance();
            if (i >= src.Length) throw Error("comment is not closed");
            Advance(2);
        }

        private SharpToken Token(int startLine, int startCol)
        {
            char c = src[i];
            if (StartsWord(c)) return Word(c, startLine, startCol);
            if (IsDigit(c)) return Number(startLine, startCol);
            if (c == '\'') return CharLiteral(startLine, startCol);
            if (StartsString(c)) return StringLiteral(c == '@', startLine, startCol);
            if (c == '$' && Ahead == '"') return Interpolated(startLine, startCol);
            return Operator(c, startLine, startCol);
        }

        private bool StartsWord(char c) => IsLetter(c) || (c == '@' && IsLetter(Ahead));

        private bool StartsString(char c) => c == '"' || (c == '@' && Ahead == '"');

        private SharpToken Word(char first, int startLine, int startCol)
        {
            if (first == '@') Advance();
            var s = new StringBuilder();
            while (i < src.Length && IsWordChar(src[i])) { s.Append(src[i]); Advance(); }
            var text = s.ToString();
            return new SharpToken { Kind = Keywords.Contains(text) && first != '@' ? "kw" : "id", Text = text, Line = startLine, Col = startCol };
        }

        private SharpToken Number(int startLine, int startCol)
        {
            var s = new StringBuilder();
            while (i < src.Length && IsWordChar(src[i])) { s.Append(src[i]); Advance(); }
            var raw = s.ToString();
            double v = NumberValue(raw);
            if (v > 65535) throw Error($"{raw} does not fit in 16 bits: SHARP-16's int runs from -32768 to 32767 (0x0000 to 0xFFFF)");
            return new SharpToken { Kind = "num", Number = (int)v, Raw = raw, Line = startLine, Col = startCol };
        }

        private double NumberValue(string raw)
        {
            var clean = raw.Replace("_", "");
            if (Regex.IsMatch(clean, "^0[xX][0-9a-fA-F]+$")) return Parse(clean.Substring(2), 16);
            if (Regex.IsMatch(clean, "^0[bB][01]+$")) return Parse(clean.Substring(2), 2);
            if (Regex.IsMatch(clean, "^[0-9]+$")) return Parse(clean, 10);
            if (Regex.IsMatch(clean, "^[0-9]+[uUlL]+$")) throw Error($"'{raw}': only 16-bit int is supported, drop the suffix");
            throw Error($"'{raw}' is not a number");
        }

        private static double Parse(string digits, int radix)
        {
            double v = 0;
            foreach (var d in digits) v = v * radix + HexValue(d);
            return v;
        }

        private static int HexValue(char d)
        {
            if (IsDigit(d)) return d - '0';
            return char.ToLowerInvariant(d) - 'a' + 10;
        }

        private int Escape()
        {
            Advance();
            bool atEnd = i >= src.Length;
            char e = Current;
            if (!atEnd && (e == 'u' || e == 'x')) return HexEscape(e);
            if (atEnd || !Escapes.TryGetValue(e, out var value)) throw Error($"unknown escape '\\{(atEnd ? "undefined" : e.ToString())}'");
            Advance();
            return value;
        }

        private int HexEscape(char e)
        {
            Advance();
            var hex = new StringBuilder();
            while (hex.Length < 4 && i < src.Length && IsHex(src[i])) { hex.Append(src[i]); Advance(); }
            if (hex.Length == 0) throw Error($"bad escape '\\{e}'");
            return (int)Parse(hex.ToString(), 16);
        }

        private SharpToken CharLiteral(int startLine, int startCol)
        {
            Advance();
            int v;
            if (Current == '\\' && !AtEnd) v = Escape();
            else
            {
                if (AtEnd || src[i] == '\'' || src[i] == '\n') throw Error("empty character literal");
                v = src[i];
                Advance();
            }
            if (AtEnd || src[i] != '\'') throw Error("a character literal holds one character");
            Advance();
            return new SharpToken { Kind = "char", Number = v, Line = startLine, Col = startCol };
        }

        private SharpToken StringLiteral(bool verbatim, int startLine, int startCol)
        {
            Advance(verbatim ? 2 : 1);
            var s = new StringBuilder();
            while (StringPart(verbatim, s)) { }
            return new SharpToken { Kind = "str", Text = s.ToString(), Line = startLine, Col = startCol };
        }

        private bool StringPart(bool verbatim, StringBuilder s)
        {
            if (AtEnd || (!verbatim && src[i] == '\n')) throw Error("string is not closed");
            if (src[i] == '"') return VerbatimQuote(verbatim, s);
            if (!verbatim && src[i] == '\\') { s.Append((char)Escape()); return true; }
            s.Append(src[i]);
            Advance();
            return true;
        }

        private bool VerbatimQuote(bool verbatim, StringBuilder s)
        {
            if (verbatim && Ahead == '"' && i + 1 < src.Length)
            {
                s.Append('"');
                Advance(2);
                return true;
            }
            Advance();
            return false;
        }

        private SharpToken Interpolated(int startLine, int startCol)
        {
            Advance(2);
            var parts = new List<SharpInterpolationPiece>();
            var text = new StringBuilder();
            while (InterpolationPart(parts, text)) { }
            if (text.Length > 0) parts.Add(new SharpInterpolationPiece { Text = text.ToString() });
            return new SharpToken { Kind = "interp", Pieces = parts, Line = startLine, Col = startCol };
        }

        private bool InterpolationPart(List<SharpInterpolationPiece> parts, StringBuilder text)
        {
            if (AtEnd || src[i] == '\n') throw Error("interpolated string is not closed");
            char ch = src[i];
            if (ch == '"') { Advance(); return false; }
            if (ch == '\\') { text.Append((char)Escape()); return true; }
            if ((ch == '{' || ch == '}') && Ahead == ch && i + 1 < src.Length) { text.Append(ch); Advance(2); return true; }
            if (ch == '{') { Hole(parts, text); return true; }
            text.Append(ch);
            Advance();
            return true;
        }

        private void Hole(List<SharpInterpolationPiece> parts, StringBuilder text)
        {
            if (text.Length > 0) { parts.Add(new SharpInterpolationPiece { Text = text.ToString() }); text.Clear(); }
            Advance();
            int eLine = line, eCol = col;
            int depth = 0;
            var e = new StringBuilder();
            while (i < src.Length && !(depth == 0 && src[i] == '}')) depth = HoleChar(e, depth);
            Advance();
            parts.Add(new SharpInterpolationPiece { Source = e.ToString(), Line = eLine, Col = eCol });
        }

        private int HoleChar(StringBuilder e, int depth)
        {
            char c = src[i];
            if (c == '\n') throw Error("interpolated string is not closed");
            depth = Depth(c, depth);
            if (depth == 0 && (c == ':' || c == ',')) throw Error("format and alignment specifiers in {…} are not supported");
            if (c == '"' || c == '\'') throw Error("put strings and characters in a variable before using them inside {…}");
            e.Append(c);
            Advance();
            return depth;
        }

        private static int Depth(char c, int depth)
        {
            return c switch
            {
                '(' or '[' => depth + 1,
                ')' or ']' => depth - 1,
                _ => depth,
            };
        }

        private SharpToken Operator(char c, int startLine, int startCol)
        {
            var op = Operators.FirstOrDefault(o => string.CompareOrdinal(src, i, o, 0, o.Length) == 0 && i + o.Length <= src.Length);
            if (op == null) throw Error($"unexpected '{c}'");
            Advance(op.Length);
            return new SharpToken { Kind = "op", Text = op, Line = startLine, Col = startCol };
        }
    }
}
