using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public class DecoderRom
    {
        public const int StepBits = 16;
        public const int AddressSpace = 1 << StepBits;
        public const int StatusVariants = 32;
        public const int StatusMask = StatusVariants - 1;
        private readonly Dictionary<int, List<MicroInstruction>> microInstructionsByRomAddress = new Dictionary<int, List<MicroInstruction>>();
        private readonly Dictionary<string, int> baseAddressByMnemonic = new Dictionary<string, int>();
        private int opCodesUsed;
        private readonly List<(InstructionDefinition Instruction, int Base, int Count)> ranges = new List<(InstructionDefinition, int, int)>();
        private readonly List<List<MicroStep>[]> variants = new List<List<MicroStep>[]>();

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

        public static int BlockSize(InstructionDefinition instruction)
        {
            return BlockSize(Enumerable.Range(0, StatusVariants).Select(instruction.StepsFor));
        }
        private static int BlockSize(IEnumerable<List<MicroStep>> byStatus) { return Math.Max(1, byStatus.Max(v => v.Count)); }

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
            if (!microInstructionsByRomAddress.TryGetValue(opCode, out var microInstructions)) microInstructionsByRomAddress[opCode] = microInstructions = new List<MicroInstruction>();
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
        public static int RomAddress(int status, int step) { return ((status & StatusMask) << StepBits) | (step & (AddressSpace - 1)); }
        public IReadOnlyList<(InstructionDefinition Instruction, int Base, int Count)> Blocks { get { return ranges; } }
        public int OpCodesUsed { get { return opCodesUsed; } }

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
        public int? OperandCount(string mnemonic)
        {
            return Microcode.FindInstruction(mnemonic)?.OperandCount;
        }
        public List<MicroInstruction> FetchInstruction(int StatusRegisterValue, int InstructionRegisterValue)
        {
            int fullOpCode = RomAddress(StatusRegisterValue, InstructionRegisterValue);
            return microInstructionsByRomAddress.TryGetValue(fullOpCode, out var microInstructions)
                ? new List<MicroInstruction>(microInstructions)
                : new List<MicroInstruction>();
        }
        public double OpCodeAddressSpaceUsedInPercent()
        {
            return Math.Round(((double)opCodesUsed / AddressSpace * 100d), 1);
        }
    }
}
