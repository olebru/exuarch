using System;
using System.Linq;
namespace Exuarch.Core
{
    internal enum LineState { Start, AfterLabel, ExpectOperand, AfterOperand, ExpectTerm, AfterTerm, AfterBracket }

    internal enum TokenRole { None, Comment, Label, Mnemonic, Operand, Comma, Open, Term, Operator, Close, Increment }

    internal readonly record struct Transition(LineState Next, TokenRole Role, string Error = null);

    internal static class LineGrammar
    {
        private const string Unexpected = "unexpected '{0}'";
        private const string LabelFirst = "a label must come first on the line";
        private const string UnexpectedComma = "unexpected ','";
        private const string NeedsMnemonic = "'{0}' needs a mnemonic before it";
        private const string MissingComma = "missing ',' before '{0}'";
        private const string OnlyInBrackets = "'{0}' is only allowed inside [ ], as in [R1 + 4]";
        private const string MissingTerm = "missing a register, number or label before '{0}'";
        private const string MissingOperator = "missing '+' or '-' before '{0}'";
        private const string MissingClose = "missing ']' before '{0}'";

        private static readonly TokenKind[] Literals = { TokenKind.Number, TokenKind.Character, TokenKind.String };
        private static readonly TokenKind[] Terms = Literals.Append(TokenKind.LabelReference).ToArray();
        private static readonly LineState[] Outside = { LineState.Start, LineState.AfterLabel, LineState.ExpectOperand, LineState.AfterOperand };
        private static readonly LineState[] Inside = { LineState.ExpectTerm, LineState.AfterTerm };

        private static readonly Transition[,] Transitions = Build();

        private static Transition[,] Build()
        {
            var table = new Transition[Enum.GetValues<LineState>().Length, Enum.GetValues<TokenKind>().Length];
            void Set(LineState state, TokenKind kind, Transition transition) => table[(int)state, (int)kind] = transition;

            foreach (var state in Enum.GetValues<LineState>())
            {
                foreach (var kind in Enum.GetValues<TokenKind>()) Set(state, kind, new Transition(state, TokenRole.None, Unexpected));
                Set(state, TokenKind.Error, new Transition(state, TokenRole.None));
                Set(state, TokenKind.Comment, new Transition(state, TokenRole.Comment));
                Set(state, TokenKind.Label, new Transition(state, TokenRole.None, LabelFirst));
                Set(state, TokenKind.Comma, new Transition(state, TokenRole.Comma, UnexpectedComma));
            }
            Set(LineState.Start, TokenKind.Label, new Transition(LineState.AfterLabel, TokenRole.Label));
            Set(LineState.AfterOperand, TokenKind.Comma, new Transition(LineState.ExpectOperand, TokenRole.Comma));
            Set(LineState.AfterBracket, TokenKind.Comma, new Transition(LineState.ExpectOperand, TokenRole.Comma));

            foreach (var state in new[] { LineState.Start, LineState.AfterLabel })
            {
                foreach (var kind in Literals.Append(TokenKind.OpenBracket)) Set(state, kind, new Transition(state, TokenRole.None, NeedsMnemonic));
                Set(state, TokenKind.LabelReference, new Transition(LineState.ExpectOperand, TokenRole.Mnemonic));
            }
            foreach (var kind in Terms)
            {
                Set(LineState.ExpectOperand, kind, new Transition(LineState.AfterOperand, TokenRole.Operand));
                Set(LineState.AfterOperand, kind, new Transition(LineState.AfterOperand, TokenRole.Operand, MissingComma));
                Set(LineState.AfterBracket, kind, new Transition(LineState.AfterOperand, TokenRole.Operand, MissingComma));
                Set(LineState.ExpectTerm, kind, new Transition(LineState.AfterTerm, TokenRole.Term));
                Set(LineState.AfterTerm, kind, new Transition(LineState.AfterTerm, TokenRole.Term, MissingOperator));
            }
            Set(LineState.ExpectOperand, TokenKind.OpenBracket, new Transition(LineState.ExpectTerm, TokenRole.Open));
            Set(LineState.AfterOperand, TokenKind.OpenBracket, new Transition(LineState.ExpectTerm, TokenRole.Open, MissingComma));
            Set(LineState.AfterBracket, TokenKind.OpenBracket, new Transition(LineState.ExpectTerm, TokenRole.Open, MissingComma));
            foreach (var state in Outside)
            {
                Set(state, TokenKind.Plus, new Transition(state, TokenRole.None, OnlyInBrackets));
                Set(state, TokenKind.CloseBracket, new Transition(state, TokenRole.None, Unexpected));
            }
            foreach (var kind in new[] { TokenKind.Plus, TokenKind.Minus })
            {
                Set(LineState.AfterTerm, kind, new Transition(LineState.ExpectTerm, TokenRole.Operator));
                Set(LineState.ExpectTerm, kind, new Transition(LineState.ExpectTerm, TokenRole.None, MissingTerm));
            }
            Set(LineState.AfterTerm, TokenKind.CloseBracket, new Transition(LineState.AfterBracket, TokenRole.Close));
            Set(LineState.ExpectTerm, TokenKind.CloseBracket, new Transition(LineState.AfterBracket, TokenRole.Close, MissingTerm));
            Set(LineState.AfterBracket, TokenKind.Plus, new Transition(LineState.AfterOperand, TokenRole.Increment));
            foreach (var state in Inside)
            {
                Set(state, TokenKind.Comma, new Transition(LineState.ExpectOperand, TokenRole.Comma, MissingClose));
                Set(state, TokenKind.OpenBracket, new Transition(state, TokenRole.None, Unexpected));
            }
            return table;
        }

        private static readonly Action<LineReader, SourceToken>[] Roles =
        {
            (reader, token) => { },
            (reader, token) => reader.Line.Comment = token,
            (reader, token) => reader.Line.Label = token,
            (reader, token) => reader.SetMnemonic(token),
            (reader, token) => reader.AddOperand(token),
            (reader, token) => reader.PendingComma = token,
            (reader, token) => reader.Open(token),
            (reader, token) => reader.AddTerm(token),
            (reader, token) => reader.PendingSign = token,
            (reader, token) => reader.Close(token),
            (reader, token) => reader.Line.Written[^1].Increment = token,
        };

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
            if (Inside.Contains(reader.State)) diagnostics.Error(line, reader.OpenBracket, "missing ']' at the end of the line");
        }

        private sealed class LineReader
        {
            public LineReader(ParsedLine line)
            {
                Line = line;
            }

            public ParsedLine Line { get; }
            public LineState State { get; set; }
            public SourceToken PendingComma { get; set; }
            public SourceToken PendingSign { get; set; }
            public SourceToken OpenBracket { get; private set; }

            public void SetMnemonic(SourceToken token)
            {
                token.Kind = token.Text.StartsWith(".") ? TokenKind.Directive : TokenKind.Mnemonic;
                Line.Mnemonic = token;
            }

            public void AddOperand(SourceToken token)
            {
                var operand = new WrittenOperand();
                operand.Terms.Add(new WrittenTerm { Token = token });
                Line.Written.Add(operand);
                Line.Operands.Add(token);
                PendingComma = null;
            }

            public void Open(SourceToken token)
            {
                OpenBracket = token;
                Line.Written.Add(new WrittenOperand { Open = token });
                PendingComma = null;
            }

            public void AddTerm(SourceToken token)
            {
                Line.Written[^1].Terms.Add(new WrittenTerm { Sign = PendingSign, Token = token });
                Line.Operands.Add(token);
                PendingSign = null;
            }

            public void Close(SourceToken token)
            {
                Line.Written[^1].Close = token;
                PendingSign = null;
            }
        }
    }
}
