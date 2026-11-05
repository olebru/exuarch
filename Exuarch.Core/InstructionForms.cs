using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    internal enum WrittenKind { Register, Literal, Label, Other }

    public sealed class InstructionForms
    {
        private const int Exact = 3;
        private const int Accepted = 2;
        private const int Tolerated = 1;
        private const int NoMatch = -1;
        private const int FormsListedInAnError = 6;

        public static readonly InstructionForms None = new InstructionForms(Enumerable.Empty<InstructionDefinition>());

        private readonly List<InstructionForm> forms;

        public InstructionForms(IEnumerable<InstructionDefinition> instructions)
        {
            forms = instructions.Select(i => InstructionForm.Parse(i, out _)).Where(f => f != null).ToList();
        }

        public IReadOnlyList<InstructionForm> All { get { return forms; } }

        public IEnumerable<string> Mnemonics { get { return forms.Select(f => f.Mnemonic).Distinct(StringComparer.OrdinalIgnoreCase); } }

        public IReadOnlyList<InstructionForm> Named(string mnemonic)
        {
            return forms.Where(f => string.Equals(f.Mnemonic, mnemonic, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        internal void Resolve(ParsedLine line, Func<string, bool> isInstruction, int registerCount, DiagnosticBag diagnostics)
        {
            if (line.Mnemonic?.Kind != TokenKind.Mnemonic) return;
            bool plain = line.Written.All(w => w.IsPlain);
            bool direct = isInstruction(line.Mnemonic.Text);
            if (direct && plain) return;
            var candidates = Named(line.Mnemonic.Text);
            if (line.SyntaxErrors.Count > 0) line.HasNoMatchingForm = candidates.Count > 0 || direct;
            else if (candidates.Count > 0) Choose(line, candidates, registerCount, diagnostics);
            else if (direct) RejectBrackets(line, diagnostics);
        }

        private static void RejectBrackets(ParsedLine line, DiagnosticBag diagnostics)
        {
            line.HasNoMatchingForm = true;
            var bracket = line.Written.First(w => !w.IsPlain);
            diagnostics.Error(line, bracket.Open ?? bracket.Terms.FirstOrDefault()?.Token ?? line.Mnemonic,
                $"{line.Mnemonic.Text} takes its operands as they are, without [ ]: the instruction has no syntax with brackets");
        }

        private static void Choose(ParsedLine line, IReadOnlyList<InstructionForm> candidates, int registerCount, DiagnosticBag diagnostics)
        {
            var scored = candidates.Select(form => (Form: form, Score: Score(form, line, registerCount))).Where(s => s.Score >= 0).ToList();
            if (scored.Count == 0)
            {
                line.HasNoMatchingForm = true;
                diagnostics.Error(line, line.Mnemonic, $"no form of {line.Mnemonic.Text} matches these operands. {line.Mnemonic.Text} is written {FormList(candidates)}");
                return;
            }
            int best = scored.Max(s => s.Score);
            var winners = scored.Where(s => s.Score == best).Select(s => s.Form).ToList();
            if (winners.Count > 1)
            {
                line.HasNoMatchingForm = true;
                diagnostics.Error(line, line.Mnemonic, $"these operands fit more than one form of {line.Mnemonic.Text}: {string.Join(" and ", winners.Select(w => $"{w.Text()} ({w.Instruction.Mnemonic})"))}");
                return;
            }
            Bind(line, winners[0]);
        }

        private static string FormList(IReadOnlyList<InstructionForm> candidates)
        {
            var listed = candidates.Take(FormsListedInAnError).Select(f => f.Text());
            var more = candidates.Count > FormsListedInAnError ? $", and {candidates.Count - FormsListedInAnError} more" : "";
            return string.Join(" · ", listed) + more;
        }

        private static void Bind(ParsedLine line, InstructionForm form)
        {
            var operands = new SourceToken[form.Instruction.OperandCount ?? 0];
            var writtenTerms = line.Written.SelectMany(w => w.Terms).ToList();
            var formTerms = form.Terms.ToList();
            for (int i = 0; i < formTerms.Count; i++)
            {
                if (formTerms[i].Operand is not int index) continue;
                operands[index] = writtenTerms[i].Token;
                if (writtenTerms[i].IsNegated) line.NegatedOperands.Add(writtenTerms[i].Token);
            }
            line.Form = form;
            line.InstructionMnemonic = form.Instruction.Mnemonic;
            line.InstructionOperands = operands.ToList();
        }

        private static int Score(InstructionForm form, ParsedLine line, int registerCount)
        {
            if (form.Operands.Count != line.Written.Count) return NoMatch;
            int total = 0;
            for (int i = 0; i < form.Operands.Count; i++)
            {
                var expected = form.Operands[i];
                var written = line.Written[i];
                if (expected.IsBracketed != written.IsBracketed || expected.IsPostIncrement != written.IsPostIncrement || expected.Terms.Count != written.Terms.Count) return NoMatch;
                for (int t = 0; t < expected.Terms.Count; t++)
                {
                    int score = TermScore(form.Instruction, expected.Terms[t], written.Terms[t], registerCount);
                    if (score < 0) return NoMatch;
                    total += score;
                }
            }
            return total;
        }

        private static readonly Dictionary<(OperandType?, WrittenKind), int> Fit = new Dictionary<(OperandType?, WrittenKind), int>
        {
            [(OperandType.Register, WrittenKind.Register)] = Exact,
            [(OperandType.Value, WrittenKind.Literal)] = Exact,
            [(OperandType.Value, WrittenKind.Label)] = Tolerated,
            [(OperandType.Address, WrittenKind.Label)] = Exact,
            [(OperandType.Address, WrittenKind.Literal)] = Accepted,
            [(OperandType.Relative, WrittenKind.Label)] = Exact,
            [(OperandType.Relative, WrittenKind.Literal)] = Accepted,
            [(null, WrittenKind.Register)] = Tolerated,
            [(null, WrittenKind.Literal)] = Tolerated,
            [(null, WrittenKind.Label)] = Tolerated,
        };

        private static int TermScore(InstructionDefinition instruction, FormTerm expected, WrittenTerm written, int registerCount)
        {
            if (expected.Word != null) return WordScore(expected.Word, written);
            var kind = Classify(written.Token, registerCount);
            if (written.IsNegated && kind != WrittenKind.Literal) return NoMatch;
            var type = instruction.OperandTypeAt(expected.Operand.Value);
            return Fit.GetValueOrDefault((type, kind), NoMatch);
        }

        private static int WordScore(string word, WrittenTerm written)
        {
            bool same = written.Token.Kind == TokenKind.LabelReference && string.Equals(written.Token.Name, word, StringComparison.OrdinalIgnoreCase);
            return same && !written.IsNegated ? Exact : NoMatch;
        }

        private static WrittenKind Classify(SourceToken token, int registerCount)
        {
            return token.Kind switch
            {
                TokenKind.LabelReference => Assembler.TryRegister(token, registerCount, out _) ? WrittenKind.Register : WrittenKind.Label,
                TokenKind.Number or TokenKind.Character => WrittenKind.Literal,
                _ => WrittenKind.Other,
            };
        }
    }
}
