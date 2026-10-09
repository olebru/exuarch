using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public class FieldDefinition
    {
        public int Low { get; set; }
        public int Bits { get; set; }
    }

    public readonly record struct FieldSlot(int Shift, int Width)
    {
        public int Mask => (1 << Width) - 1;
        public int High => Shift + Width - 1;
        public int Read(int word) => (word >> Shift) & Mask;
        public int ReadSigned(int word)
        {
            int value = Read(word);
            return (value & (1 << (Width - 1))) != 0 ? (value - (1 << Width)) & Bus.Mask : value;
        }
        public bool Fits(int cell) => cell <= Mask || cell >= Bus.Mask + 1 - (1 << (Width - 1));
        public int Place(int cell) => (cell & Mask) << Shift;
        public bool Overlaps(FieldSlot other) => Shift <= other.High && other.Shift <= High;
    }

    public sealed class InstructionFormat
    {
        public const int MinOpcodeBits = 1;
        public const int MaxOpcodeBits = 15;
        public const int MaxFields = 4;

        private readonly List<InstructionDefinition> instructions;
        private readonly InstructionDefinition fetch;

        private InstructionFormat(MicrocodeDefinition microcode, int opcodeBits)
        {
            OpcodeBits = opcodeBits;
            instructions = microcode.Instructions.ToList();
            fetch = microcode.Fetch;
            Slots = (microcode.Fields ?? new List<FieldDefinition>()).Take(MaxFields).Select(f => new FieldSlot(f.Low, f.Bits)).ToList();
        }

        public static InstructionFormat Of(MicrocodeDefinition microcode)
        {
            return microcode?.OpcodeBits is int bits && bits >= MinOpcodeBits && bits <= MaxOpcodeBits ? new InstructionFormat(microcode, bits) : null;
        }

        public int OpcodeBits { get; }
        public int OpcodeShift => Bus.Width - OpcodeBits;
        public int OpcodeCount => 1 << OpcodeBits;
        public IReadOnlyList<FieldSlot> Slots { get; }

        public const int FetchOpcode = 0;

        public int? Opcode(InstructionDefinition instruction)
        {
            if (instruction != null && instruction == fetch) return FetchOpcode;
            int index = instructions.IndexOf(instruction);
            return index < 0 ? null : (index + 1) << OpcodeShift;
        }

        public int OpcodeNumber(int word) => (word & Bus.Mask) >> OpcodeShift;

        public InstructionDefinition InstructionOf(int word)
        {
            int index = OpcodeNumber(word) - 1;
            return index >= 0 && index < instructions.Count ? instructions[index] : null;
        }

        public FieldSlot? SlotOfOperand(InstructionDefinition instruction, int operand)
        {
            var fields = instruction?.Fields;
            if (fields == null || operand >= fields.Count) return null;
            int field = fields[operand];
            return field >= 0 && field < Slots.Count ? Slots[field] : null;
        }

        public const int NextCell = -1;

        public int FieldOperandCount(InstructionDefinition instruction) => instruction?.Fields?.Count(field => field != NextCell) ?? 0;

        public const char Unused = '-';

        public static char OperandLetter(int operand) => (char)('a' + operand);

        public string Picture(InstructionDefinition instruction)
        {
            var bits = Bits(instruction);
            var groups = new List<string>();
            int start = 0;
            for (int index = 1; index <= Bus.Width; index++)
            {
                if (index < Bus.Width && !StartsGroup(bits, index)) continue;
                groups.Add(new string(bits, start, index - start));
                start = index;
            }
            return string.Join(" ", groups);
        }

        private bool StartsGroup(char[] bits, int index)
        {
            return index == OpcodeBits || (index > OpcodeBits && bits[index] != bits[index - 1]);
        }

        private char[] Bits(InstructionDefinition instruction)
        {
            var bits = new char[Bus.Width];
            int opcode = Opcode(instruction) ?? 0;
            for (int bit = 0; bit < Bus.Width; bit++) bits[Bus.Width - 1 - bit] = bit < OpcodeShift ? Unused : (((opcode >> bit) & 1) == 1 ? '1' : '0');
            for (int operand = 0; operand < (instruction?.Fields?.Count ?? 0); operand++) MarkOperand(bits, instruction, operand);
            return bits;
        }

        private void MarkOperand(char[] bits, InstructionDefinition instruction, int operand)
        {
            if (SlotOfOperand(instruction, operand) is not FieldSlot slot) return;
            for (int bit = slot.Shift; bit <= slot.High; bit++) bits[Bus.Width - 1 - bit] = OperandLetter(operand);
        }

        public static (int[] Cells, string[] Operands) Disassemble(InstructionFormat format, InstructionDefinition instruction, Func<int, int> cell)
        {
            int operands = instruction.OperandCount ?? 0;
            int inWord = format?.FieldOperandCount(instruction) ?? 0;
            var cells = Enumerable.Range(0, Math.Max(1, operands - inWord + 1)).Select(cell).ToArray();
            int next = 1;
            var shown = new string[operands];
            for (int operand = 0; operand < operands; operand++)
            {
                if (format?.SlotOfOperand(instruction, operand) is FieldSlot field) shown[operand] = field.Read(cells[0]).ToString();
                else shown[operand] = next < cells.Length ? cells[next++].ToString("X4") : "?";
            }
            return (cells, shown);
        }

        public string Placement(InstructionDefinition instruction)
        {
            int operands = instruction.OperandCount ?? 0;
            var parts = new List<string>();
            for (int operand = 0; operand < operands; operand++)
            {
                parts.Add(SlotOfOperand(instruction, operand) is FieldSlot slot
                    ? $"{OperandLetter(operand)} = operand {operand + 1}, bits {slot.High}-{slot.Shift}"
                    : $"operand {operand + 1} in the next cell");
            }
            return string.Join(" · ", parts);
        }
    }
}
