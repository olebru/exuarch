using System.Collections.Generic;
using System.Linq;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Microcoding
{
    // Where the instructions are in the decoder ROM: blocks laid out in order from 0, one micro step address for each
    // step of an instruction's longest flag variant.
    public static class OpcodeLayout
    {
        public const int AddressSpace = DecoderRom.AddressSpace;

        public static int Used(MicrocodeDefinition microcode) => microcode.AllInstructions.Sum(DecoderRom.BlockSize);

        // The opcode the decoder ROM gives each instruction.
        public static Dictionary<InstructionDefinition, string> Opcodes(MicrocodeDefinition microcode)
        {
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
