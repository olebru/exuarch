using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    internal enum CompletionPlace { Nothing, Mnemonic, Register, Operand, RegisterOrOperand }

    internal readonly record struct CompletionContext(CompletionPlace Place, InstructionDefinition Instruction = null, OperandType? Expected = null);

    internal sealed class AssemblyCompletion
    {
        private static readonly HashSet<TokenKind> Literals = new HashSet<TokenKind> { TokenKind.Number, TokenKind.Character, TokenKind.String };

        private readonly IEnumerable<InstructionDefinition> instructions;
        private readonly InstructionForms forms;
        private readonly int registerCount;
        private readonly Dictionary<CompletionPlace, Func<SemanticModel, CompletionContext, List<CompletionItem>>> providers;

        public AssemblyCompletion(IEnumerable<InstructionDefinition> instructions, InstructionForms forms, int registerCount)
        {
            this.instructions = instructions;
            this.forms = forms;
            this.registerCount = registerCount;
            providers = new Dictionary<CompletionPlace, Func<SemanticModel, CompletionContext, List<CompletionItem>>>
            {
                [CompletionPlace.Nothing] = (model, context) => new List<CompletionItem>(),
                [CompletionPlace.Mnemonic] = (model, context) => Mnemonics(),
                [CompletionPlace.Register] = (model, context) => Registers(context),
                [CompletionPlace.Operand] = Labels,
                [CompletionPlace.RegisterOrOperand] = (model, context) => Registers(context).Concat(Labels(model, context)).ToList(),
            };
        }

        public List<CompletionItem> Complete(SemanticModel model, int lineNumber, int column)
        {
            var context = Classify(model.Line(lineNumber), column);
            return providers[context.Place](model, context);
        }

        private CompletionContext Classify(BoundLine line, int column)
        {
            if (line == null || InComment(line.Syntax, column)) return new CompletionContext(CompletionPlace.Nothing);
            if (AtMnemonic(line.Syntax, column)) return new CompletionContext(CompletionPlace.Mnemonic);
            if (InLiteral(line.Syntax, column)) return new CompletionContext(CompletionPlace.Nothing);
            return InOperands(line, line.Syntax.Operands.Count(o => o.End < column));
        }

        private CompletionContext InOperands(BoundLine line, int written)
        {
            if (line.Syntax.Form is InstructionForm form) return InForm(form, written);
            if (line.Instruction == null && forms.Named(line.Syntax.Mnemonic.Text).Count > 0)
            {
                return new CompletionContext(CompletionPlace.RegisterOrOperand, new InstructionDefinition { Mnemonic = line.Syntax.Mnemonic.Text });
            }
            if (line.Instruction?.OperandCount == 0) return new CompletionContext(CompletionPlace.Nothing);
            return Expecting(line.Instruction, line.Instruction?.OperandTypeAt(written));
        }

        private static CompletionContext InForm(InstructionForm form, int written)
        {
            var term = form.Terms.ElementAtOrDefault(written);
            if (term?.Operand is not int index) return new CompletionContext(CompletionPlace.Nothing);
            return Expecting(form.Instruction, form.Instruction.OperandTypeAt(index));
        }

        private static CompletionContext Expecting(InstructionDefinition instruction, OperandType? expected)
        {
            return new CompletionContext(expected == OperandType.Register ? CompletionPlace.Register : CompletionPlace.Operand, instruction, expected);
        }

        private static bool InComment(ParsedLine line, int column)
        {
            return line.Comment != null && column > line.Comment.Start;
        }

        private static bool AtMnemonic(ParsedLine line, int column)
        {
            return line.Mnemonic == null || column <= line.Mnemonic.End;
        }

        private static bool InLiteral(ParsedLine line, int column)
        {
            var current = line.TokenAt(column - 1);
            return current != null && Literals.Contains(current.Kind);
        }

        private List<CompletionItem> Mnemonics()
        {
            var names = new HashSet<string>(instructions.Select(i => i.Mnemonic), StringComparer.OrdinalIgnoreCase);
            var instructionItems = instructions.Select(i => new CompletionItem
            {
                Label = i.Mnemonic,
                Kind = CompletionKind.Instruction,
                Detail = InstructionText.OperandSummary(i),
                Documentation = i.Description ?? "",
                InsertText = i.Mnemonic + ((i.OperandCount ?? 0) > 0 ? " " : ""),
            });
            var formItems = forms.Mnemonics.Where(m => !names.Contains(m)).Select(m => new CompletionItem
            {
                Label = m,
                Kind = CompletionKind.Instruction,
                Detail = Words.Count(forms.Named(m).Count, "form"),
                Documentation = InstructionText.FormList(forms.Named(m)),
                InsertText = m + " ",
            });
            return instructionItems.Concat(formItems).OrderBy(item => item.Label)
                .Concat(AssemblyDirectives.Current.Select(d => d.Completion())).ToList();
        }

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
            var documentation = context.Expected == OperandType.Value ? $"{context.Instruction.Mnemonic} takes a value here; a label stands for its address." : "";
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
