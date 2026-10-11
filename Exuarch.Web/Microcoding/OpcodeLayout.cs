using System.Collections.Generic;
using System.Linq;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Microcoding
{
    public static class OpcodeLayout
    {
        public const int AddressSpace = DecoderRom.AddressSpace;

        public static int Used(MicrocodeDefinition microcode) => microcode.AllInstructions.Sum(DecoderRom.BlockSize);

        public static Dictionary<InstructionDefinition, string> Opcodes(MicrocodeDefinition microcode)
        {
            var format = InstructionFormat.Of(microcode);
            if (format != null) return microcode.AllInstructions.ToDictionary(i => i, i => format.OpcodeNumber(format.Opcode(i) ?? 0).ToString("X2"));
            var opcodes = new Dictionary<InstructionDefinition, string>();
            int address = 0;
            foreach (var instruction in microcode.AllInstructions)
            {
                opcodes[instruction] = Formats.Hex(address);
                address += DecoderRom.BlockSize(instruction);
            }
            return opcodes;
        }
    }
}
