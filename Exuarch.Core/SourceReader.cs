using System;
namespace Exuarch.Core
{
    internal sealed class SourceReader
    {
        public SourceReader(string text)
        {
            Text = text;
        }

        public string Text { get; }
        public int Position { get; private set; }
        public bool AtEnd { get { return Position >= Text.Length; } }

        public char Peek(int offset = 0)
        {
            int at = Position + offset;
            return at < Text.Length ? Text[at] : '\0';
        }

        public char Advance()
        {
            return Text[Position++];
        }

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

        public SourceToken Token(TokenKind kind, int start, int[] values = null)
        {
            return new SourceToken { Kind = kind, Text = From(start), Start = start + 1, End = Position + 1, Values = values ?? Array.Empty<int>() };
        }

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
