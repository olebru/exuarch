using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Exuarch.Core
{
    public sealed class FormTerm
    {
        public int? Operand { get; init; }
        public string Word { get; init; }
    }

    public sealed class FormOperand
    {
        public bool IsBracketed { get; init; }
        public bool IsPostIncrement { get; init; }
        public List<FormTerm> Terms { get; } = new List<FormTerm>();
    }

    public sealed class InstructionForm
    {
        private InstructionForm(InstructionDefinition instruction, string mnemonic, List<FormOperand> operands)
        {
            Instruction = instruction;
            Mnemonic = mnemonic;
            Operands = operands;
        }

        public InstructionDefinition Instruction { get; }
        public string Mnemonic { get; }
        public IReadOnlyList<FormOperand> Operands { get; }
        public IEnumerable<FormTerm> Terms { get { return Operands.SelectMany(o => o.Terms); } }
        public bool IsPlain { get { return Operands.All(o => !o.IsBracketed && o.Terms.All(t => t.Operand != null)); } }

        public string Text()
        {
            var operands = string.Join(", ", Operands.Select(OperandText));
            return operands.Length == 0 ? Mnemonic : Mnemonic + " " + operands;
        }

        public string Shape()
        {
            return string.Join(",", Operands.Select(o => (o.IsBracketed ? "[" : "") + string.Join("+", o.Terms.Select(t => t.Word?.ToUpperInvariant() ?? TypeName(t.Operand.Value))) + (o.IsBracketed ? "]" : "") + (o.IsPostIncrement ? "+" : "")));
        }

        private string OperandText(FormOperand operand)
        {
            var inside = string.Join(" + ", operand.Terms.Select(t => t.Word ?? TypeName(t.Operand.Value)));
            return operand.IsBracketed ? "[" + inside + "]" + (operand.IsPostIncrement ? "+" : "") : inside;
        }

        private string TypeName(int operand)
        {
            return Instruction.OperandTypeAt(operand) is OperandType type ? OperandTypeNames.Name(type) : "operand";
        }

        public static InstructionForm Parse(InstructionDefinition instruction, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(instruction?.Syntax)) return null;
            var line = AssemblyParser.ParseLine(Placeholders.ToWords(instruction.Syntax), 1);
            problem = LineProblem(instruction, line);
            if (problem != null) return null;
            var operands = new List<FormOperand>();
            foreach (var written in line.Written)
            {
                var operand = OperandOf(instruction, written, out problem);
                if (operand == null) return null;
                operands.Add(operand);
            }
            var form = new InstructionForm(instruction, line.Mnemonic.Text, operands);
            problem = form.CheckOperands();
            return problem == null ? form : null;
        }

        private static string LineProblem(InstructionDefinition instruction, ParsedLine line)
        {
            if (line.SyntaxErrors.Count > 0) return $"its syntax '{instruction.Syntax}' does not parse: {line.SyntaxErrors[0].Message}";
            bool startsWithMnemonic = line.Label == null && line.Comment == null && line.Mnemonic?.Kind == TokenKind.Mnemonic;
            return startsWithMnemonic ? null : $"its syntax '{instruction.Syntax}' must start with a mnemonic";
        }

        private static FormOperand OperandOf(InstructionDefinition instruction, WrittenOperand written, out string problem)
        {
            problem = null;
            if (written.Terms.Any(t => t.IsNegated)) { problem = $"its syntax '{instruction.Syntax}' uses '-': write '+', and a program can then write either"; return null; }
            var odd = written.Terms.FirstOrDefault(t => t.Token.Kind != TokenKind.LabelReference);
            if (odd != null) { problem = $"its syntax '{instruction.Syntax}' has '{odd.Token.Text}': write {{0}}, {{1}} and so on for operands, or a fixed word like PC"; return null; }
            var operand = new FormOperand { IsBracketed = written.IsBracketed, IsPostIncrement = written.IsPostIncrement };
            operand.Terms.AddRange(written.Terms.Select(t => Placeholders.Index(t.Token.Name) is int index ? new FormTerm { Operand = index } : new FormTerm { Word = t.Token.Name }));
            return operand;
        }

        private string CheckOperands()
        {
            int count = Instruction.OperandCount ?? 0;
            var used = Terms.Where(t => t.Operand != null).Select(t => t.Operand.Value).ToList();
            var outside = used.FirstOrDefault(index => index >= count, -1);
            if (outside >= 0) return $"its syntax '{Instruction.Syntax}' uses {{{outside}}}, but it has {Words.Count(count, "operand")}, numbered from {{0}}";
            var twice = used.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
            if (twice != null) return $"its syntax '{Instruction.Syntax}' uses {{{twice.Key}}} more than once";
            var missing = Enumerable.Range(0, count).FirstOrDefault(i => !used.Contains(i), -1);
            if (missing >= 0) return $"its syntax '{Instruction.Syntax}' leaves out {{{missing}}}: every operand must appear once";
            return null;
        }

        private static class Placeholders
        {
            private const string Prefix = "__operand";

            public static string ToWords(string syntax)
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < syntax.Length; i++)
                {
                    int close = syntax[i] == '{' ? syntax.IndexOf('}', i) : -1;
                    if (close > i && int.TryParse(syntax.AsSpan(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    {
                        text.Append(Prefix).Append(index.ToString(CultureInfo.InvariantCulture));
                        i = close;
                    }
                    else text.Append(syntax[i]);
                }
                return text.ToString();
            }

            public static int? Index(string word)
            {
                return word.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(word.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
            }
        }
    }
}
