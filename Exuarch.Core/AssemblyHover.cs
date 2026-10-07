using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Exuarch.Core
{
    // Hover markdown for each kind of token: what it is, and for an operand what it means to its instruction.
    internal sealed class AssemblyHover
    {
        // What an operand of each type is for, after its name.
        private static readonly Dictionary<OperandType, string> Meanings = new Dictionary<OperandType, string>
        {
            [OperandType.Address] = "the memory location it uses",
            [OperandType.Register] = "a register of the register file",
            [OperandType.Value] = "used as it is",
        };

        private readonly DecoderRom rom;
        private readonly int registerCount;
        private readonly Dictionary<TokenKind, Func<SourceSymbol, string>> describers;

        // rom gives the opcodes, when the microcode makes one.
        public AssemblyHover(DecoderRom rom, int registerCount)
        {
            this.rom = rom;
            this.registerCount = registerCount;
            describers = new Dictionary<TokenKind, Func<SourceSymbol, string>>
            {
                [TokenKind.Mnemonic] = Mnemonic,
                [TokenKind.Directive] = Directive,
                [TokenKind.Label] = Label,
                // A register operand is a register name, whatever labels there are.
                [TokenKind.LabelReference] = symbol => symbol.OperandType == OperandType.Register ? Register(symbol) : Label(symbol),
                [TokenKind.Number] = Value,
                [TokenKind.Character] = Value,
                [TokenKind.String] = String,
            };
        }

        public string Describe(SourceSymbol symbol)
        {
            return symbol != null && describers.TryGetValue(symbol.Token.Kind, out var describe) ? describe(symbol) : null;
        }

        private string Mnemonic(SourceSymbol symbol)
        {
            var instruction = symbol.Line.Instruction;
            return instruction == null ? $"**{symbol.Token.Text}** is not an instruction of this machine." : InstructionMarkdown(instruction);
        }

        private static string Directive(SourceSymbol symbol)
        {
            return AssemblyDirectives.Find(symbol.Token.Text).Hover(symbol.Token.Text) + AssemblyLanguage.ReadMore;
        }

        private string Register(SourceSymbol symbol)
        {
            var register = RegisterOperand.Of(symbol.Token, registerCount);
            if (register.Number is int number) return $"register **R{number}** of the register file: the operand cell holds {number}{Role(symbol)}";
            return register.MachineHasNone
                ? "this machine has no register file for a register operand"
                : $"**{symbol.Token.Name}** is not a register: write {register.Names}";
        }

        private static string Label(SourceSymbol symbol)
        {
            var name = symbol.Token.Name;
            var label = symbol.Labels.TryGetValue(name, out var address)
                ? $"label **{name}** at `{InstructionText.Hex(address)}` ({address})"
                : $"label **{name}** is not defined";
            return label + Role(symbol);
        }

        private static string Value(SourceSymbol symbol)
        {
            int value = symbol.Token.Values[0];
            var character = value >= 0x20 && value <= 0xFF && CharacterDisplay.ToChar((byte)value) != ' ' || value == 0x20
                ? $" · '{(char)value}'" : "";
            return $"`{value}` · `{InstructionText.Hex(value)}`{character}" + Role(symbol);
        }

        private static string String(SourceSymbol symbol)
        {
            int length = symbol.Token.Values.Length;
            return $"string of {Words.Count(length, "character")}, one cell each";
        }

        // What an operand means for its instruction, from the instruction's operand types.
        private static string Role(SourceSymbol symbol)
        {
            if (symbol.OperandType is not OperandType type) return "";
            var meaning = Meanings.GetValueOrDefault(type, Meanings[OperandType.Value]);
            return $"\n\n**{OperandTypeNames.Name(type)}** for {symbol.Line.Instruction.Mnemonic}: {meaning}";
        }

        private string InstructionMarkdown(InstructionDefinition instruction)
        {
            var text = new StringBuilder();
            var operands = InstructionText.SignatureOperands(instruction);
            text.Append(operands.Length > 0 ? $"**{instruction.Mnemonic}**{operands}" : $"**{instruction.Mnemonic}** · {InstructionText.OperandSummary(instruction)}");
            if (rom != null) text.Append($" · opcode `{InstructionText.Hex(rom.FetchByteCodeFromMnemonic(instruction.Mnemonic))}`");
            text.Append("\n\n");
            if (!string.IsNullOrWhiteSpace(instruction.Description)) text.Append(instruction.Description).Append("\n\n");
            text.Append("```\n");
            for (int i = 0; i < instruction.Steps.Count; i++)
            {
                var step = instruction.Steps[i];
                var when = step.When == null ? "" : $"  (when {step.When})";
                text.Append($"{i + 1}: {string.Join(", ", step.Signals)}{when}\n");
            }
            text.Append("```");
            return text.ToString();
        }
    }

    // How the editor writes an instruction and numbers.
    internal static class InstructionText
    {
        // " address" or " value, address" after the mnemonic, when the operand types are known.
        public static string SignatureOperands(InstructionDefinition instruction)
        {
            var signature = instruction.Signature;
            return signature.Length > instruction.Mnemonic.Length ? " " + signature.Substring(instruction.Mnemonic.Length + 1) : "";
        }

        public static string OperandSummary(InstructionDefinition instruction)
        {
            if (instruction.OperandTypes != null && instruction.OperandTypes.Count > 0)
            {
                return string.Join(", ", instruction.OperandTypes.Select(OperandTypeNames.Name));
            }
            return instruction.OperandCount switch
            {
                null => "operands not declared",
                0 => "no operands",
                1 => "1 operand",
                var n => $"{n} operands",
            };
        }

        public static string Hex(int value) { return "0x" + value.ToString("X4"); }
    }
}
