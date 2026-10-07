using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // What can be typed at a position: nothing (in a comment, a literal or after an instruction without operands), a
    // mnemonic, or an operand in a register slot or any other.
    internal enum CompletionPlace { Nothing, Mnemonic, Register, Operand }

    internal readonly record struct CompletionContext(CompletionPlace Place, InstructionDefinition Instruction = null, OperandType? Expected = null);

    // Completion: the place is classified first, then the provider for that place makes the items.
    internal sealed class AssemblyCompletion
    {
        private static readonly HashSet<TokenKind> Literals = new HashSet<TokenKind> { TokenKind.Number, TokenKind.Character, TokenKind.String };

        private readonly IEnumerable<InstructionDefinition> instructions;
        private readonly int registerCount;
        private readonly Dictionary<CompletionPlace, Func<SemanticModel, CompletionContext, List<CompletionItem>>> providers;

        public AssemblyCompletion(IEnumerable<InstructionDefinition> instructions, int registerCount)
        {
            this.instructions = instructions;
            this.registerCount = registerCount;
            providers = new Dictionary<CompletionPlace, Func<SemanticModel, CompletionContext, List<CompletionItem>>>
            {
                [CompletionPlace.Nothing] = (model, context) => new List<CompletionItem>(),
                [CompletionPlace.Mnemonic] = (model, context) => Mnemonics(),
                [CompletionPlace.Register] = (model, context) => Registers(context),
                [CompletionPlace.Operand] = Labels,
            };
        }

        public List<CompletionItem> Complete(SemanticModel model, int lineNumber, int column)
        {
            var context = Classify(model.Line(lineNumber), column);
            return providers[context.Place](model, context);
        }

        private static CompletionContext Classify(BoundLine line, int column)
        {
            if (line == null || InComment(line.Syntax, column)) return new CompletionContext(CompletionPlace.Nothing);
            if (AtMnemonic(line.Syntax, column)) return new CompletionContext(CompletionPlace.Mnemonic);
            if (line.Instruction?.OperandCount == 0 || InLiteral(line.Syntax, column)) return new CompletionContext(CompletionPlace.Nothing);
            var expected = line.Instruction?.OperandTypeAt(line.Syntax.Operands.Count(o => o.End < column));
            return new CompletionContext(expected == OperandType.Register ? CompletionPlace.Register : CompletionPlace.Operand, line.Instruction, expected);
        }

        private static bool InComment(ParsedLine line, int column)
        {
            return line.Comment != null && column > line.Comment.Start;
        }

        // Before or on the mnemonic.
        private static bool AtMnemonic(ParsedLine line, int column)
        {
            return line.Mnemonic == null || column <= line.Mnemonic.End;
        }

        // Typing a literal straight before the position.
        private static bool InLiteral(ParsedLine line, int column)
        {
            var current = line.TokenAt(column - 1);
            return current != null && Literals.Contains(current.Kind);
        }

        private List<CompletionItem> Mnemonics()
        {
            return instructions.OrderBy(i => i.Mnemonic).Select(i => new CompletionItem
            {
                Label = i.Mnemonic,
                Kind = CompletionKind.Instruction,
                Detail = InstructionText.OperandSummary(i),
                Documentation = i.Description ?? "",
                InsertText = i.Mnemonic + ((i.OperandCount ?? 0) > 0 ? " " : ""),
            }).Concat(AssemblyDirectives.Current.Select(d => d.Completion())).ToList();
        }

        // A register slot takes a register name, not a label.
        private List<CompletionItem> Registers(CompletionContext context)
        {
            return Enumerable.Range(0, registerCount).Select(r => new CompletionItem
            {
                Label = $"R{r}",
                Kind = CompletionKind.Label,
                Detail = $"register {r}",
                Documentation = $"{context.Instruction.Mnemonic} takes a register of the register file here.",
                InsertText = $"R{r}",
            }).ToList();
        }

        private static List<CompletionItem> Labels(SemanticModel model, CompletionContext context)
        {
            var documentation = context.Expected == OperandType.Value ? $"{context.Instruction.Mnemonic} takes a value here; a label gives its address." : "";
            return model.Labels.OrderBy(l => l.Value).Select(l => new CompletionItem
            {
                Label = l.Key,
                Kind = CompletionKind.Label,
                Detail = $"label at {InstructionText.Hex(l.Value)}",
                Documentation = documentation,
                InsertText = l.Key,
            }).ToList();
        }
    }
}
