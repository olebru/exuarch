using System;
namespace Exuarch.Core
{
    // A cursor over one line of source for the lexer: what comes next, and tokens from a start to here.
    internal sealed class SourceReader
    {
        public SourceReader(string text)
        {
            Text = text;
        }

        public string Text { get; }
        // 0 based index of the next character.
        public int Position { get; private set; }
        public bool AtEnd { get { return Position >= Text.Length; } }

        // The character offset places after the next one, or \0 past the end of the line.
        public char Peek(int offset = 0)
        {
            int at = Position + offset;
            return at < Text.Length ? Text[at] : '\0';
        }

        public char Advance()
        {
            return Text[Position++];
        }

        // Moves past the next character when it is c.
        public bool Take(char c)
        {
            if (AtEnd || Text[Position] != c) return false;
            Position++;
            return true;
        }

        public void TakeWhile(Func<char, bool> predicate)
        {
            while (!AtEnd && predicate(Text[Position])) Position++;
        }

        public void TakeRest()
        {
            Position = Text.Length;
        }

        public string From(int start)
        {
            return Text.Substring(start, Position - start);
        }

        // A token from start up to here, with 1 based columns.
        public SourceToken Token(TokenKind kind, int start, int[] values = null)
        {
            return new SourceToken { Kind = kind, Text = From(start), Start = start + 1, End = Position + 1, Values = values ?? Array.Empty<int>() };
        }

        // An error token always covers at least one character, so the editor has something to underline.
        public SourceToken Error(int start, string message)
        {
            int end = Math.Min(Math.Max(Position, start + 1), Text.Length);
            return new SourceToken { Kind = TokenKind.Error, Text = Text.Substring(start, end - start), Start = start + 1, End = end + 1, Values = Array.Empty<int>(), Name = message };
        }

        public SourceToken TokenOrError(TokenKind kind, int start, int[] values, string error)
        {
            return error == null ? Token(kind, start, values) : Error(start, error);
        }
    }
}
