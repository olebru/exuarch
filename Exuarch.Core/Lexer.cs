using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // Splits a line of assembly into tokens. Between tokens it skips spaces and tabs; at each token the first scanner
    // that starts with the next character reads it, and what none of them reads is an error up to the next separator.
    internal static class Lexer
    {
        private delegate SourceToken Scanner(SourceReader reader);

        // A scanner and the characters a token it reads can start with. It is only called at such a character, and
        // returns null when what follows is not its token after all, like a - that is not before a digit.
        private sealed record TokenScanner(Func<char, bool> Starts, Scanner Scan);

        private static readonly TokenScanner[] Scanners =
        {
            new TokenScanner(c => c == ';', Comment),
            new TokenScanner(c => c == ',', Comma),
            new TokenScanner(c => c == '"', String),
            new TokenScanner(c => c == '#' || c == '\'' || c == '-' || char.IsDigit(c), Literal),
            new TokenScanner(AssemblyParser.IsIdentifierStart, Word),
        };

        // The scanner for each ASCII character, worked out once from the table above.
        private static readonly Scanner[] AsciiScanners = Enumerable.Range(0, 128).Select(c => ScannerFor((char)c)).ToArray();

        private const string Separators = " \t,;";

        public static IEnumerable<SourceToken> Tokenize(string text)
        {
            var reader = new SourceReader(text);
            for (reader.TakeWhile(IsSpace); !reader.AtEnd; reader.TakeWhile(IsSpace))
            {
                yield return Scan(reader);
            }
        }

        private static bool IsSpace(char c) { return c == ' ' || c == '\t'; }

        private static SourceToken Scan(SourceReader reader)
        {
            char c = reader.Peek();
            var scanner = c < AsciiScanners.Length ? AsciiScanners[c] : ScannerFor(c);
            return scanner(reader) ?? Unexpected(reader);
        }

        private static Scanner ScannerFor(char c)
        {
            return Scanners.FirstOrDefault(scanner => scanner.Starts(c))?.Scan ?? Unexpected;
        }

        // A comment runs to the end of the line.
        private static SourceToken Comment(SourceReader reader)
        {
            int start = reader.Position;
            reader.TakeRest();
            return reader.Token(TokenKind.Comment, start);
        }

        private static SourceToken Comma(SourceReader reader)
        {
            int start = reader.Position;
            reader.Advance();
            return reader.Token(TokenKind.Comma, start);
        }

        private static SourceToken String(SourceReader reader)
        {
            int start = reader.Position;
            reader.Advance();
            var text = QuotedReader.Read(reader, '"');
            return reader.TokenOrError(TokenKind.String, start, text.Values, text.Error);
        }

        // Literals: 15, 0x2A or 'A'. A leading # is allowed and means the same. A - straight before a number makes it
        // negative.
        private static SourceToken Literal(SourceReader reader)
        {
            if (reader.Peek() == '-' && !char.IsDigit(reader.Peek(1))) return null;
            int start = reader.Position;
            bool negative = !reader.Take('#') && reader.Take('-');
            return reader.Peek() == '\'' ? Character(reader, start) : Number(reader, start, negative);
        }

        private static SourceToken Character(SourceReader reader, int start)
        {
            reader.Advance();
            var character = QuotedReader.Read(reader, '\'').AsCharacter();
            return reader.TokenOrError(TokenKind.Character, start, character.Values, character.Error);
        }

        private static SourceToken Number(SourceReader reader, int start, bool negative)
        {
            int digits = reader.Position;
            reader.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_');
            var number = NumberLiteral.Parse(reader.From(start), reader.From(digits), negative);
            return reader.TokenOrError(TokenKind.Number, start, new[] { number.Value }, number.Error);
        }

        // A name: a label when a colon follows it, otherwise a mnemonic, directive or label reference, which the line
        // grammar tells apart.
        private static SourceToken Word(SourceReader reader)
        {
            int start = reader.Position;
            reader.Advance();
            reader.TakeWhile(AssemblyParser.IsIdentifierPart);
            var token = reader.Token(reader.Take(':') ? TokenKind.Label : TokenKind.LabelReference, start);
            token.Name = token.Kind == TokenKind.Label ? token.Text[..^1] : token.Text;
            return token;
        }

        private static SourceToken Unexpected(SourceReader reader)
        {
            int start = reader.Position;
            reader.TakeWhile(c => Separators.IndexOf(c) < 0);
            return reader.Error(start, $"unexpected '{reader.From(start)}'");
        }
    }
}
