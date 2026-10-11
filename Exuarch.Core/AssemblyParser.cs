using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Exuarch.Core
{
    public enum TokenKind { Label, Mnemonic, Directive, Number, Character, String, LabelReference, Comma, Comment, Error }

    public class SourceToken
    {
        public TokenKind Kind { get; set; }
        public string Text { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int[] Values { get; set; } = Array.Empty<int>();
        public string Name { get; set; }
        public bool Contains(int column) { return column >= Start && column <= End; }
    }

    public class AssemblyDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Error;
        public int Line { get; set; }
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
        public string Message { get; set; }
        public string Text { get; set; }
        public override string ToString() { return $"Line {Line}: {Message}: '{Text}'"; }
    }

    public class ParsedLine
    {
        public int Number { get; set; }
        public string Text { get; set; }
        public SourceToken Label { get; set; }
        public SourceToken Mnemonic { get; set; }
        public List<SourceToken> Operands { get; } = new List<SourceToken>();
        public SourceToken Comment { get; set; }
        public List<SourceToken> Tokens { get; } = new List<SourceToken>();
        public List<AssemblyDiagnostic> SyntaxErrors { get; } = new List<AssemblyDiagnostic>();
        public bool IsDirective { get { return Mnemonic?.Kind == TokenKind.Directive; } }
        public SourceToken TokenAt(int column)
        {
            return Tokens.FirstOrDefault(t => t.Contains(column) && t.Kind != TokenKind.Comma);
        }
    }

    public static class AssemblyParser
    {
        public static List<ParsedLine> Parse(string source)
        {
            return SourceText.SplitLines(source ?? "").Select((text, index) => ParseLine(text, index + 1)).ToList();
        }

        public static ParsedLine ParseLine(string text, int lineNumber)
        {
            var line = new ParsedLine { Number = lineNumber, Text = text };
            var diagnostics = new DiagnosticBag(line.SyntaxErrors);
            line.Tokens.AddRange(Lexer.Tokenize(text));
            foreach (var token in line.Tokens)
            {
                if (token.Kind == TokenKind.Error) diagnostics.Error(line, token, token.Name);
            }
            LineGrammar.Read(line, diagnostics);
            return line;
        }

        public static bool TryParseNumber(string text, out int value)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) && value >= 0;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        public static bool IsIdentifierStart(char c) { return char.IsLetter(c) || c == '_' || c == '.'; }
        public static bool IsIdentifierPart(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
    }
}
