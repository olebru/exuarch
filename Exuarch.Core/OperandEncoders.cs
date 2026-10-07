using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // Where an operand stands: in a .DATA style directive, or in an instruction slot of a declared type or none.
    internal enum OperandSlot { Data, Untyped, Value, Address, Register }

    internal static class OperandSlots
    {
        private static readonly Dictionary<OperandType, OperandSlot> Typed = new Dictionary<OperandType, OperandSlot>
        {
            [OperandType.Value] = OperandSlot.Value,
            [OperandType.Address] = OperandSlot.Address,
            [OperandType.Register] = OperandSlot.Register,
        };

        // An instruction slot of a type the assembler does not know reads like an untyped one.
        public static OperandSlot Of(OperandType? type)
        {
            return type is OperandType known && Typed.TryGetValue(known, out var slot) ? slot : OperandSlot.Untyped;
        }
    }

    // Adds an operand's cells, reporting what is wrong with it.
    internal delegate void OperandEncoder(AssemblyContext context, ParsedLine line, SourceToken operand);

    // The encoder for each slot and kind of operand.
    internal static class OperandEncoders
    {
        private static readonly TokenKind[] OperandKinds = { TokenKind.Number, TokenKind.Character, TokenKind.String, TokenKind.LabelReference };

        private static readonly Dictionary<(OperandSlot, TokenKind), OperandEncoder> Encoders = Build();

        public static OperandEncoder For(OperandSlot slot, TokenKind kind)
        {
            return Encoders[(slot, kind)];
        }

        // Each kind of operand has its rule, and some slots read it differently: a register slot takes a register name
        // whatever labels there are (anywhere else R1 is a label), only data takes strings, and a value slot warns
        // about a label's address.
        private static Dictionary<(OperandSlot, TokenKind), OperandEncoder> Build()
        {
            var table = new Dictionary<(OperandSlot, TokenKind), OperandEncoder>();
            foreach (var slot in Enum.GetValues<OperandSlot>())
            {
                table[(slot, TokenKind.Number)] = Literal;
                table[(slot, TokenKind.Character)] = Literal;
                table[(slot, TokenKind.String)] = StringOutsideData;
                table[(slot, TokenKind.LabelReference)] = Label;
            }
            table[(OperandSlot.Data, TokenKind.String)] = String;
            table[(OperandSlot.Value, TokenKind.LabelReference)] = LabelAsValue;
            foreach (var kind in OperandKinds) table[(OperandSlot.Register, kind)] = Register;
            return table;
        }

        private static void Literal(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            if (operand.Values[0] > Bus.Mask) context.Diagnostics.Error(line, operand, $"'{operand.Text}' is not a number between 0 and {Bus.Mask}");
            context.Cells.Add(operand.Values[0] & Bus.Mask);
        }

        private static void String(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            context.Cells.AddRange(operand.Values);
        }

        private static void StringOutsideData(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            context.Diagnostics.Error(line, operand, "a string is only allowed in .DATA and .STRING");
            String(context, line, operand);
        }

        private static void Label(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            TryLabel(context, line, operand, out _);
        }

        private static void LabelAsValue(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            if (!TryLabel(context, line, operand, out var address)) return;
            context.Diagnostics.Warning(line, operand, $"{line.Mnemonic.Text} takes a value here, and '{operand.Name}' is the address of a label (0x{address:X4}). Use a number, or an instruction that takes an address.");
        }

        // Adds the label's address, or 0 and an error when there is no such label.
        private static bool TryLabel(AssemblyContext context, ParsedLine line, SourceToken operand, out int address)
        {
            bool known = context.Labels.TryGetValue(operand.Name, out address);
            if (!known) context.Diagnostics.Error(line, operand, $"unknown label '{operand.Name}'");
            context.Cells.Add(address);
            return known;
        }

        private static void Register(AssemblyContext context, ParsedLine line, SourceToken operand)
        {
            var register = RegisterOperand.Of(operand, context.RegisterCount);
            if (register.Number == null)
            {
                context.Diagnostics.Error(line, operand, register.MachineHasNone
                    ? $"{line.Mnemonic.Text} takes a register here, but the machine has no register file"
                    : $"{line.Mnemonic.Text} takes a register here: write {register.Names}, not '{operand.Text}'");
            }
            context.Cells.Add(register.Number ?? 0);
        }
    }
}
