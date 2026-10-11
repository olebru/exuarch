using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
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

        public BoundLine Line(int number)
        {
            return number >= 1 && number <= lines.Count ? new BoundLine(lines[number - 1], instructionOf) : null;
        }

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
        public InstructionDefinition Instruction { get; }

        public OperandType? TypeOf(SourceToken operand)
        {
            int index = Syntax.Operands.IndexOf(operand);
            return index < 0 || Syntax.IsDirective ? null : Instruction?.OperandTypeAt(index);
        }
    }

    internal sealed record SourceSymbol(BoundLine Line, SourceToken Token, IReadOnlyDictionary<string, int> Labels)
    {
        public OperandType? OperandType { get { return Line.TypeOf(Token); } }
    }
}
