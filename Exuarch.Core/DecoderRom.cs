using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // The compiled microcode. An address is the 5 bits of the decoder status (I, N, V, C, Z) above the 16 bit
    // micro step address held in the instruction register. Each instruction gets a block of consecutive step
    // addresses, sized for its longest flag variant.
    public class DecoderRom
    {
        // The micro step register is 16 bits, so there are 2^16 step addresses per flag value.
        public const int StepBits = 16;
        public const int AddressSpace = 1 << StepBits;
        // The four flags and the interrupt request: bits 0 to 4 of the decoder status.
        public const int StatusVariants = 32;
        public const int StatusMask = StatusVariants - 1;
        // The ROM image: the micro instructions at each full ROM address.
        private readonly Dictionary<int, List<MicroInstruction>> romByOpCode = new Dictionary<int, List<MicroInstruction>>();
        private readonly Dictionary<string, int> baseAddressByMnemonic = new Dictionary<string, int>();
        private int opCodesUsed;
        private readonly List<(InstructionDefinition Instruction, int Base, int Count)> ranges = new List<(InstructionDefinition, int, int)>();
        // The steps each instruction runs for each of the 32 status values, parallel to ranges.
        private readonly List<List<MicroStep>[]> variants = new List<List<MicroStep>[]>();

        // Accepts microcode JSON, or the legacy tab separated format.
        public DecoderRom(string microcode) : this(MicrocodeDefinition.Parse(microcode))
        {
        }

        public DecoderRom(MicrocodeDefinition microcode)
        {
            Microcode = microcode;
            Format = InstructionFormat.Of(microcode);
            foreach (var instruction in microcode.AllInstructions)
            {
                int block = Allocate(instruction);
                Burn(instruction, block);
            }
            if (opCodesUsed > AddressSpace)
            {
                throw new Exception($"OpCode AddressSpace is exhausted, {opCodesUsed} opcodes needed but only {AddressSpace} available, optimize...");
            }
        }

        // Gives the instruction the next block of step addresses, as many as its longest flag variant has steps.
        private int Allocate(InstructionDefinition instruction)
        {
            var mnemonic = instruction.Mnemonic ?? "";
            if (baseAddressByMnemonic.ContainsKey(mnemonic))
            {
                throw new ArgumentException($"Mnemonic '{instruction.Mnemonic}' is defined more than once.");
            }
            int block = opCodesUsed;
            var byStatus = Enumerable.Range(0, StatusVariants).Select(s => instruction.StepsFor(s)).ToArray();
            baseAddressByMnemonic[mnemonic] = block;
            ranges.Add((instruction, block, BlockSize(byStatus)));
            variants.Add(byStatus);
            opCodesUsed += ranges[^1].Count;
            return block;
        }

        // The micro step addresses an instruction takes: as many as its longest flag variant has steps, and at least one.
        public static int BlockSize(InstructionDefinition instruction)
        {
            return BlockSize(Enumerable.Range(0, StatusVariants).Select(instruction.StepsFor));
        }
        private static int BlockSize(IEnumerable<List<MicroStep>> byStatus) { return Math.Max(1, byStatus.Max(v => v.Count)); }

        // Writes each step's signals into the image at every status value it runs for. Each step's signals are
        // parsed once.
        private void Burn(InstructionDefinition instruction, int block)
        {
            var parsed = new Dictionary<MicroStep, Signal[]>();
            var byStatus = variants[^1];
            for (int status = 0; status < StatusVariants; status++)
            {
                for (int step = 0; step < byStatus[status].Count; step++)
                {
                    var microStep = byStatus[status][step];
                    if (!parsed.TryGetValue(microStep, out var signals)) parsed[microStep] = signals = Parse(instruction, step, microStep);
                    int opCode = (status << StepBits) | (block + step);
                    foreach (var signal in signals)
                    {
                        Image(opCode).Add(new MicroInstruction(opCode, signal.Device, signal.Line, instruction.Mnemonic, false, step == 0 && status == 0));
                    }
                }
            }
        }
        private static Signal[] Parse(InstructionDefinition instruction, int step, MicroStep microStep)
        {
            return microStep.Signals.Select(text => Signal.TryParse(text, out var signal)
                ? signal
                : throw new FormatException($"{instruction.Mnemonic} step {step}: '{text}' is not a signal, write it as device.line")).ToArray();
        }
        private List<MicroInstruction> Image(int opCode)
        {
            if (!romByOpCode.TryGetValue(opCode, out var microInstructions)) romByOpCode[opCode] = microInstructions = new List<MicroInstruction>();
            return microInstructions;
        }

        public MicrocodeDefinition Microcode { get; }
        public InstructionFormat Format { get; }

        public InstructionDefinition InstructionOfWord(int word)
        {
            return Format != null ? Format.InstructionOf(word) : ranges.FirstOrDefault(r => r.Base == word).Instruction;
        }

        public int StepFor(int instructionWord)
        {
            var instruction = Format?.InstructionOf(instructionWord);
            if (instruction == null) return 0;
            return ranges.First(r => r.Instruction == instruction).Base;
        }
        // Full ROM address for a decoder status and micro step: (status & 0x1F) << StepBits | step.
        public static int RomAddress(int status, int step) { return ((status & StatusMask) << StepBits) | (step & (AddressSpace - 1)); }
        // Each instruction's block of micro step addresses, in address order.
        public IReadOnlyList<(InstructionDefinition Instruction, int Base, int Count)> Blocks { get { return ranges; } }
        public int OpCodesUsed { get { return opCodesUsed; } }

        // The instruction whose micro step block contains the address, and the step that runs there for
        // the given status flags (null when that flag variant has no step at this offset).
        public (InstructionDefinition Instruction, MicroStep Step, int Offset)? Locate(int statusRegisterValue, int instructionRegisterValue)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (instructionRegisterValue < range.Base || instructionRegisterValue >= range.Base + range.Count) continue;
                int offset = instructionRegisterValue - range.Base;
                var variant = variants[i][statusRegisterValue & StatusMask];
                return (range.Instruction, offset < variant.Count ? variant[offset] : null, offset);
            }
            return null;
        }
        public int FetchByteCodeFromMnemonic(string Mnemonic)
        {
            if (Mnemonic == null || !baseAddressByMnemonic.TryGetValue(Mnemonic, out var baseAddress))
            {
                throw new ArgumentException($"Unknown mnemonic '{Mnemonic}', it is not defined in the decoder ROM.");
            }
            return Format?.Opcode(Microcode.FindInstruction(Mnemonic)) ?? baseAddress;
        }
        // Operand cells the instruction declares, or null when it does not say.
        public int? OperandCount(string mnemonic)
        {
            return Microcode.FindInstruction(mnemonic)?.OperandCount;
        }
        // The decoder has 5 status inputs (I, N, V, C and Z), so status bits above bit 4 are ignored.
        public List<MicroInstruction> FetchInstruction(int StatusRegisterValue, int InstructionRegisterValue)
        {
            int fullOpCode = RomAddress(StatusRegisterValue, InstructionRegisterValue);
            return romByOpCode.TryGetValue(fullOpCode, out var microInstructions)
                ? new List<MicroInstruction>(microInstructions)
                : new List<MicroInstruction>();
        }
        public double OpCodeAddressSpaceUsedInPercent()
        {
            return Math.Round(((double)opCodesUsed / AddressSpace * 100d), 1);
        }
    }
}
