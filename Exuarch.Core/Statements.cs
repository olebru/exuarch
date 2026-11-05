using System;
using System.Linq;
namespace Exuarch.Core
{
    internal abstract class Statement
    {
        protected Statement(ParsedLine line)
        {
            Line = line;
        }

        public ParsedLine Line { get; }
        public virtual bool IsListed { get { return true; } }
        protected virtual bool IsInstruction { get { return false; } }

        public abstract int CellCount();
        public abstract void Emit(AssemblyContext context);

        public static Statement For(ParsedLine line, InstructionSet instructions)
        {
            if (line.Mnemonic == null) return new LabelStatement(line);
            return line.IsDirective ? new DirectiveStatement(line, AssemblyDirectives.Find(line.Mnemonic.Text)) : new InstructionStatement(line, instructions);
        }

        public ListingLine ToListing(int address, int[] cells)
        {
            return new ListingLine
            {
                LineNumber = Line.Number,
                Text = Line.Text,
                Label = Line.Label?.Name,
                Mnemonic = Line.Mnemonic?.Text,
                Operands = Line.Written.Select(o => o.IsPlain ? o.Terms[0].Token.Text : o.Text()).ToArray(),
                Address = address,
                Cells = cells,
                IsInstruction = IsInstruction,
            };
        }

        protected int OperandCells()
        {
            return Line.BoundOperands.Sum(o => o.Kind == TokenKind.String ? o.Values.Length : 1);
        }

        protected void EmitOperands(AssemblyContext context, Func<int, OperandSlot> slotAt)
        {
            for (int index = 0; index < Line.BoundOperands.Count; index++)
            {
                var operand = Line.BoundOperands[index];
                if (Line.NegatedOperands.Contains(operand)) operand = Negated(operand);
                OperandEncoders.For(slotAt(index), operand.Kind)(context, Line, operand);
            }
        }

        private static SourceToken Negated(SourceToken operand)
        {
            return new SourceToken
            {
                Kind = operand.Kind,
                Text = "-" + operand.Text.TrimStart('#'),
                Start = operand.Start,
                End = operand.End,
                Values = new[] { -operand.Values[0] & Bus.Mask },
                Name = operand.Name,
            };
        }
    }

    internal sealed class LabelStatement : Statement
    {
        public LabelStatement(ParsedLine line) : base(line)
        {
        }

        public override bool IsListed { get { return Line.Label != null; } }
        public override int CellCount() { return 0; }
        public override void Emit(AssemblyContext context) { }
    }

    internal sealed class InstructionStatement : Statement
    {
        private readonly InstructionSet instructions;

        public InstructionStatement(ParsedLine line, InstructionSet instructions) : base(line)
        {
            this.instructions = instructions;
        }

        protected override bool IsInstruction { get { return true; } }
        public override int CellCount()
        {
            if (Line.HasNoMatchingForm) return 1 + Line.Operands.Count;
            return 1 + OperandCells() - instructions.FieldOperands(Line.ResolvedMnemonic, Line.BoundOperands.Count);
        }

        public override void Emit(AssemblyContext context)
        {
            if (Line.HasNoMatchingForm)
            {
                context.Cells.AddRange(new int[CellCount()]);
                return;
            }
            var mnemonic = Line.ResolvedMnemonic;
            var opcode = instructions.Opcode(mnemonic);
            if (opcode == null) context.Diagnostics.Error(Line, Line.Mnemonic, $"unknown mnemonic '{mnemonic}'");
            else CheckOperandCount(context);
            int opcodeCell = context.Cells.Count;
            context.InstructionEnd = opcodeCell + CellCount();
            context.Cells.Add(opcode ?? 0);
            EmitOperands(context, index => OperandSlots.Of(instructions.OperandType(mnemonic, index)));
            PackFields(context, mnemonic, opcodeCell);
        }

        private void PackFields(AssemblyContext context, string mnemonic, int opcodeCell)
        {
            int cell = opcodeCell + 1;
            var operands = Line.BoundOperands;
            for (int index = 0; index < operands.Count; index++)
            {
                if (instructions.Field(mnemonic, index) is not FieldSlot field)
                {
                    cell += operands[index].Kind == TokenKind.String ? operands[index].Values.Length : 1;
                    continue;
                }
                int value = context.Cells[cell];
                context.Cells.RemoveAt(cell);
                CheckFit(context, operands[index], field, value, instructions.OperandType(mnemonic, index) == OperandType.Relative);
                context.Cells[opcodeCell] |= field.Place(value);
            }
        }

        private void CheckFit(AssemblyContext context, SourceToken operand, FieldSlot field, int value, bool isDistance)
        {
            var place = $"the {field.Width} bit field at bits {field.High}-{field.Shift}";
            if (isDistance && !field.FitsSigned(value))
            {
                int distance = (short)value;
                context.Diagnostics.Error(Line, operand, $"'{operand.Text}' is {Words.Count(distance, "cell")} away, which does not fit in {place}: it holds a distance from {field.SignedLowest} to {field.SignedHighest}");
            }
            else if (!isDistance && !field.Fits(value))
            {
                context.Diagnostics.Error(Line, operand, $"'{operand.Text}' does not fit in {place}: it takes 0 to {field.Mask}, or -{1 << (field.Width - 1)} to -1");
            }
        }

        private void CheckOperandCount(AssemblyContext context)
        {
            var expected = instructions.OperandCount(Line.ResolvedMnemonic);
            if (expected is not int count || count == Line.BoundOperands.Count) return;
            context.Diagnostics.Error(Line, Line.Mnemonic, $"{Line.Mnemonic.Text} takes {Words.Count(count, "operand")}, found {Line.BoundOperands.Count}");
        }
    }

    internal sealed class DirectiveStatement : Statement
    {
        private readonly AssemblyDirective directive;

        public DirectiveStatement(ParsedLine line, AssemblyDirective directive) : base(line)
        {
            this.directive = directive;
        }

        public override int CellCount() { return OperandCells() + directive.Terminator.Length; }

        public override void Emit(AssemblyContext context)
        {
            directive.Report(context.Diagnostics, Line);
            if (Line.HasBracketedOperands) context.Diagnostics.Error(Line, Line.Written.First(o => o.IsBracketed).Open, $"{Line.Mnemonic.Text} stores values as they are: [ ] is only for instruction operands");
            EmitOperands(context, index => OperandSlot.Data);
            context.Cells.AddRange(directive.Terminator);
        }
    }
}
