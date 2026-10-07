using System;
using System.Linq;
namespace Exuarch.Core
{
    // A parsed line as the assembler sees it: how many cells it takes, and how it makes them.
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
        // Adds the line's cells to the context, and what is wrong with them to its diagnostics.
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
                Operands = Line.Operands.Select(o => o.Text).ToArray(),
                Address = address,
                Cells = cells,
                IsInstruction = IsInstruction,
            };
        }

        // One cell per operand, and one per character for a string.
        protected int OperandCells()
        {
            return Line.Operands.Sum(o => o.Kind == TokenKind.String ? o.Values.Length : 1);
        }

        protected void EmitOperands(AssemblyContext context, Func<int, OperandSlot> slotAt)
        {
            for (int index = 0; index < Line.Operands.Count; index++)
            {
                var operand = Line.Operands[index];
                OperandEncoders.For(slotAt(index), operand.Kind)(context, Line, operand);
            }
        }
    }

    // A line with only a label, a comment or nothing: no cells. It is listed when it has a label.
    internal sealed class LabelStatement : Statement
    {
        public LabelStatement(ParsedLine line) : base(line)
        {
        }

        public override bool IsListed { get { return Line.Label != null; } }
        public override int CellCount() { return 0; }
        public override void Emit(AssemblyContext context) { }
    }

    // The opcode, then a cell per operand.
    internal sealed class InstructionStatement : Statement
    {
        private readonly InstructionSet instructions;

        public InstructionStatement(ParsedLine line, InstructionSet instructions) : base(line)
        {
            this.instructions = instructions;
        }

        protected override bool IsInstruction { get { return true; } }
        public override int CellCount() { return 1 + OperandCells(); }

        public override void Emit(AssemblyContext context)
        {
            var mnemonic = Line.Mnemonic.Text;
            var opcode = instructions.Opcode(mnemonic);
            if (opcode == null) context.Diagnostics.Error(Line, Line.Mnemonic, $"unknown mnemonic '{mnemonic}'");
            else CheckOperandCount(context);
            context.Cells.Add(opcode ?? 0);
            EmitOperands(context, index => OperandSlots.Of(instructions.OperandType(mnemonic, index)));
        }

        private void CheckOperandCount(AssemblyContext context)
        {
            var expected = instructions.OperandCount(Line.Mnemonic.Text);
            if (expected is not int count || count == Line.Operands.Count) return;
            context.Diagnostics.Error(Line, Line.Mnemonic, $"{Line.Mnemonic.Text} takes {Words.Count(count, "operand")}, found {Line.Operands.Count}");
        }
    }

    // Data: a cell per operand or character, then the directive's terminator.
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
            EmitOperands(context, index => OperandSlot.Data);
            context.Cells.AddRange(directive.Terminator);
        }
    }
}
