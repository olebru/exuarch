using System;
using System.Linq;
namespace Exuarch.Core
{
    // Where a line is in its structure: [label:] [MNEMONIC [operand, operand ...]] [; comment].
    internal enum LineState { Start, AfterLabel, ExpectOperand, AfterOperand }

    // What a token turns out to be on its line.
    internal enum TokenRole { None, Comment, Label, Mnemonic, Operand, Comma }

    // From a state and the next token: the state after it, the token's role, and the error when it does not belong
    // there ({0} is the token's text). A token in the wrong place still takes its role when the line can use it.
    internal readonly record struct Transition(LineState Next, TokenRole Role, string Error = null);

    // The line grammar as a transition table, read token by token.
    internal static class LineGrammar
    {
        private const string LabelFirst = "a label must come first on the line";
        private const string UnexpectedComma = "unexpected ','";
        private const string NeedsMnemonic = "'{0}' needs a mnemonic before it";
        private const string MissingComma = "missing ',' before '{0}'";

        private static readonly TokenKind[] Literals = { TokenKind.Number, TokenKind.Character, TokenKind.String };

        // Indexed by state and token kind.
        private static readonly Transition[,] Transitions = Build();

        private static Transition[,] Build()
        {
            var table = new Transition[Enum.GetValues<LineState>().Length, Enum.GetValues<TokenKind>().Length];
            foreach (var state in Enum.GetValues<LineState>())
            {
                // The lexer has reported its errors already.
                table[(int)state, (int)TokenKind.Error] = new Transition(state, TokenRole.None);
                table[(int)state, (int)TokenKind.Comment] = new Transition(state, TokenRole.Comment);
                table[(int)state, (int)TokenKind.Label] = new Transition(state, TokenRole.None, LabelFirst);
                table[(int)state, (int)TokenKind.Comma] = new Transition(state, TokenRole.Comma, UnexpectedComma);
            }
            table[(int)LineState.Start, (int)TokenKind.Label] = new Transition(LineState.AfterLabel, TokenRole.Label);
            table[(int)LineState.AfterOperand, (int)TokenKind.Comma] = new Transition(LineState.ExpectOperand, TokenRole.Comma);

            // Before the mnemonic a name is the mnemonic (or directive); after it, names and literals are operands.
            foreach (var state in new[] { LineState.Start, LineState.AfterLabel })
            {
                foreach (var kind in Literals) table[(int)state, (int)kind] = new Transition(state, TokenRole.None, NeedsMnemonic);
                table[(int)state, (int)TokenKind.LabelReference] = new Transition(LineState.ExpectOperand, TokenRole.Mnemonic);
            }
            foreach (var kind in Literals.Append(TokenKind.LabelReference))
            {
                table[(int)LineState.ExpectOperand, (int)kind] = new Transition(LineState.AfterOperand, TokenRole.Operand);
                table[(int)LineState.AfterOperand, (int)kind] = new Transition(LineState.AfterOperand, TokenRole.Operand, MissingComma);
            }
            return table;
        }

        // What each role does to the line, in TokenRole order.
        private static readonly Action<LineReader, SourceToken>[] Roles =
        {
            (reader, token) => { },
            (reader, token) => reader.Line.Comment = token,
            (reader, token) => reader.Line.Label = token,
            (reader, token) => reader.SetMnemonic(token),
            (reader, token) => reader.AddOperand(token),
            (reader, token) => reader.PendingComma = token,
        };

        // Gives the line's tokens their roles, reporting tokens that are out of place.
        public static void Read(ParsedLine line, DiagnosticBag diagnostics)
        {
            var reader = new LineReader(line);
            foreach (var token in line.Tokens)
            {
                var transition = Transitions[(int)reader.State, (int)token.Kind];
                if (transition.Error != null) diagnostics.Error(line, token, string.Format(transition.Error, token.Text));
                Roles[(int)transition.Role](reader, token);
                reader.State = transition.Next;
            }
            if (reader.PendingComma != null) diagnostics.Error(line, reader.PendingComma, "missing operand after ','");
        }

        private sealed class LineReader
        {
            public LineReader(ParsedLine line)
            {
                Line = line;
            }

            public ParsedLine Line { get; }
            public LineState State { get; set; }
            // The last comma, until an operand follows it.
            public SourceToken PendingComma { get; set; }

            public void SetMnemonic(SourceToken token)
            {
                token.Kind = token.Text.StartsWith(".") ? TokenKind.Directive : TokenKind.Mnemonic;
                Line.Mnemonic = token;
            }

            public void AddOperand(SourceToken token)
            {
                Line.Operands.Add(token);
                PendingComma = null;
            }
        }
    }
}
