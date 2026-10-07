using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // What the editor knows about one version of the source: its lines and label addresses, read once and shared by
    // hover and completion. A line is bound to the machine's instructions each time it is asked for, so an
    // instruction set that changed since is read as it is now.
    internal sealed class SemanticModel
    {
        private readonly List<ParsedLine> lines;
        private readonly Func<string, InstructionDefinition> instructionOf;

        public SemanticModel(string source, AssemblyContext declared, Func<string, InstructionDefinition> instructionOf)
        {
            Source = source;
            this.instructionOf = instructionOf;
            lines = declared.Result.Lines;
            Labels = declared.Labels;
        }

        public string Source { get; }
        public IReadOnlyDictionary<string, int> Labels { get; }

        // A 1 based line with the instruction its mnemonic names, or null past the end.
        public BoundLine Line(int number)
        {
            return number >= 1 && number <= lines.Count ? new BoundLine(lines[number - 1], instructionOf) : null;
        }

        // The token under a position and what it means there, or null.
        public SourceSymbol SymbolAt(int lineNumber, int column)
        {
            var line = Line(lineNumber);
            var token = line?.Syntax.TokenAt(column);
            return token == null ? null : new SourceSymbol(line, token, Labels);
        }
    }

    internal sealed class BoundLine
    {
        public BoundLine(ParsedLine syntax, Func<string, InstructionDefinition> instructionOf)
        {
            Syntax = syntax;
            Instruction = syntax.Mnemonic == null ? null : instructionOf(syntax.Mnemonic.Text);
        }

        public ParsedLine Syntax { get; }
        // The instruction the mnemonic names, or null when there is none.
        public InstructionDefinition Instruction { get; }

        // The type the instruction takes for an operand of the line, or null when it does not say.
        public OperandType? TypeOf(SourceToken operand)
        {
            int index = Syntax.Operands.IndexOf(operand);
            return index < 0 || Syntax.IsDirective ? null : Instruction?.OperandTypeAt(index);
        }
    }

    // A token of the source with its line and the program's labels.
    internal sealed record SourceSymbol(BoundLine Line, SourceToken Token, IReadOnlyDictionary<string, int> Labels)
    {
        public OperandType? OperandType { get { return Line.TypeOf(Token); } }
    }
}
