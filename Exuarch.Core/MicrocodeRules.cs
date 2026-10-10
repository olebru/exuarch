using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // What the microcode rules share while they check one microcode against one machine.
    internal sealed class MicrocodeContext
    {
        public MicrocodeContext(MicrocodeDefinition microcode, MachineDefinition machine, DeviceRegistry registry, Machine built)
        {
            Microcode = microcode;
            Machine = machine;
            Registry = registry;
            Signals = new SignalResolver(machine, registry, built);
            Ends = InstructionEnds.Of(machine, registry);
        }
        public MicrocodeDefinition Microcode { get; }
        public MachineDefinition Machine { get; }
        public DeviceRegistry Registry { get; }
        public SignalResolver Signals { get; }
        // The signals that end an instruction, or null when the machine names no micro step register.
        public InstructionEnds Ends { get; }
        public List<MicrocodeDiagnostic> Diagnostics { get; } = new List<MicrocodeDiagnostic>();
        // Mnemonics seen so far, and the opcodes the instructions so far take.
        public HashSet<string> Mnemonics { get; } = new HashSet<string>();
        public int OpCodes { get; set; }

        public void Error(InstructionDefinition instruction, int? step, string signal, string message)
        {
            Add(DiagnosticSeverity.Error, instruction, step, signal, message);
        }
        public void Warning(InstructionDefinition instruction, int? step, string signal, string message)
        {
            Add(DiagnosticSeverity.Warning, instruction, step, signal, message);
        }
        private void Add(DiagnosticSeverity severity, InstructionDefinition instruction, int? step, string signal, string message)
        {
            Diagnostics.Add(new MicrocodeDiagnostic { Severity = severity, Instruction = instruction?.Mnemonic, Step = step, Signal = signal, Message = message });
        }
    }

    // One micro step being checked, with its signals each looked up once, in order and without repeats.
    internal sealed class StepContext
    {
        public StepContext(MicrocodeContext microcode, InstructionDefinition instruction, int index)
        {
            Instruction = instruction;
            Index = index;
            Texts = instruction.Steps[index].Signals;
            Resolved = Texts.Distinct().Select(microcode.Signals.Resolve).ToList();
        }
        public InstructionDefinition Instruction { get; }
        public int Index { get; }
        public List<string> Texts { get; }
        public List<ResolvedSignal> Resolved { get; }
    }

    // The signals that end an instruction: those that send the micro step register back to fetch, and those that
    // stop the halt clock. Without a halt clock named, any clock's stop is taken as the end.
    internal sealed class InstructionEnds
    {
        private readonly HashSet<string> signals = new HashSet<string>();

        public string StepRegister { get; }

        private InstructionEnds(string stepRegister, IEnumerable<string> clocks)
        {
            StepRegister = stepRegister;
            AddSignals(stepRegister, DeviceRole.InstructionRegister);
            foreach (var clock in clocks) AddSignals(clock, DeviceRole.Halt);
        }

        public static InstructionEnds Of(MachineDefinition machine, DeviceRegistry registry)
        {
            var stepRegister = DeviceRole.InstructionRegister.DeviceIn(machine);
            if (stepRegister == null) return null;
            var halt = DeviceRole.Halt.DeviceIn(machine);
            var clocks = halt != null ? new[] { halt } : machine.Devices.Where(d => registry.Info(d.Type)?.ControlLines.Any(l => l.Halts) == true).Select(d => d.Id);
            return new InstructionEnds(stepRegister, clocks);
        }

        private void AddSignals(string device, DeviceRole role)
        {
            foreach (var line in role.EndingLines)
            {
                var text = $"{device}.{line}";
                if (Signal.TryParse(text, out _)) signals.Add(text);
            }
        }

        public bool Ends(MicroStep step) { return step.Signals.Any(signals.Contains); }
    }

    internal interface IMicrocodeRule
    {
        void Check(MicrocodeContext context);
    }
    internal interface IInstructionRule
    {
        void Check(MicrocodeContext context, InstructionDefinition instruction);
    }
    internal interface IStepRule
    {
        void Check(MicrocodeContext context, StepContext step);
    }

    // The rules in the order their problems are listed.
    internal static class MicrocodeRules
    {
        public static readonly IStepRule[] StepRules =
        {
            new NoRepeatedSignals(), new SignalsExist(), new OneDriverPerBus(), new ReadBusesAreDriven(), new OneWriterOfTheFlags(),
        };
        public static readonly IInstructionRule[] InstructionRules =
        {
            new MnemonicIsOneUniqueWord(), new HasSteps(), new OperandsAreNotNegative(), new OperandTypesMatchOperands(), new TakesOpCodes(),
            new EachStep(StepRules), new ReturnsToFetch(), new RegisterOperandsHaveARegisterFile(), new OperandsUseFieldsOfTheWord(),
        };
        public static readonly IMicrocodeRule[] Rules =
        {
            new FetchIsRequired(), new OpcodeFieldHoldsEveryInstruction(), new FieldsFitBelowTheOpcode(), new EachInstruction(InstructionRules), new OpCodesFit(),
        };

        public static void Run(MicrocodeContext context)
        {
            foreach (var rule in Rules) rule.Check(context);
        }
    }

    internal sealed class FetchIsRequired : IMicrocodeRule
    {
        public void Check(MicrocodeContext context)
        {
            if (context.Microcode.Fetch == null) context.Error(null, null, null, "a fetch routine is required, it runs at opcode 0 to load the next instruction.");
        }
    }

    internal sealed class OpcodeFieldHoldsEveryInstruction : IMicrocodeRule
    {
        public void Check(MicrocodeContext context)
        {
            var microcode = context.Microcode;
            if (microcode.OpcodeBits is not int bits)
            {
                if (microcode.Fields?.Count > 0 || microcode.Instructions.Any(i => i.Fields?.Count > 0))
                    context.Error(null, null, null, "has operand fields but no opcodeBits: fields are read from the instruction word only when the decoder takes its opcode from the top bits of the word.");
                return;
            }
            if (bits < InstructionFormat.MinOpcodeBits || bits > InstructionFormat.MaxOpcodeBits)
            {
                context.Error(null, null, null, $"opcodeBits must be between {InstructionFormat.MinOpcodeBits} and {InstructionFormat.MaxOpcodeBits}, not {bits}.");
                return;
            }
            int count = microcode.Instructions.Count;
            int available = (1 << bits) - 1;
            if (count > available)
                context.Error(null, null, null, $"has {Words.Count(count, "instruction")}, but an opcode of {bits} bits has room for {available}: opcode 0 is fetch.");
        }
    }

    internal sealed class FieldsFitBelowTheOpcode : IMicrocodeRule
    {
        public void Check(MicrocodeContext context)
        {
            var fields = context.Microcode.Fields;
            if (fields == null || context.Microcode.OpcodeBits is not int bits) return;
            if (fields.Count > InstructionFormat.MaxFields)
                context.Error(null, null, null, $"has {fields.Count} fields, but an instruction word has lines for at most {InstructionFormat.MaxFields}.");
            int below = Bus.Width - bits;
            for (int index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.Bits < 1 || field.Low < 0 || field.Low + field.Bits > below)
                    context.Error(null, null, null, $"field {index} must lie in bits {below - 1} to 0, below the {bits} opcode bits, and be at least 1 bit wide.");
            }
        }
    }

    internal sealed class OperandsUseFieldsOfTheWord : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            var fields = instruction.Fields;
            if (fields == null || fields.Count == 0 || context.Microcode.OpcodeBits == null) return;
            if (instruction == context.Microcode.Fetch)
            {
                context.Error(instruction, null, null, "the fetch routine has no operands to put in fields.");
                return;
            }
            int operands = instruction.OperandCount ?? 0;
            if (fields.Count > operands)
                context.Error(instruction, null, null, $"puts {Words.Count(fields.Count, "operand")} in fields, but takes {Words.Count(operands, "operand")}.");
            CheckFieldsUsed(context, instruction, context.Microcode.Fields ?? new List<FieldDefinition>());
        }

        private static void CheckFieldsUsed(MicrocodeContext context, InstructionDefinition instruction, List<FieldDefinition> defined)
        {
            var used = new List<(int Field, FieldSlot Slot)>();
            foreach (var field in instruction.Fields.Where(f => f != InstructionFormat.NextCell))
            {
                if (field < 0 || field >= defined.Count)
                {
                    context.Error(instruction, null, null, $"uses field {field}, but the microcode defines {Words.Count(defined.Count, "field")}.");
                    continue;
                }
                var slot = new FieldSlot(defined[field].Low, defined[field].Bits);
                foreach (var other in used.Where(u => u.Field == field || u.Slot.Overlaps(slot))) ReportShared(context, instruction, other.Field, field);
                used.Add((field, slot));
            }
        }

        private static void ReportShared(MicrocodeContext context, InstructionDefinition instruction, int first, int second)
        {
            context.Error(instruction, null, null, first == second
                ? $"puts two operands in field {second}."
                : $"puts operands in fields {first} and {second}, which share bits of the word.");
        }
    }

    internal sealed class EachInstruction : IMicrocodeRule
    {
        private readonly IInstructionRule[] rules;
        public EachInstruction(IInstructionRule[] rules) { this.rules = rules; }
        public void Check(MicrocodeContext context)
        {
            foreach (var instruction in context.Microcode.AllInstructions)
            {
                foreach (var rule in rules) rule.Check(context, instruction);
            }
        }
    }

    internal sealed class OpCodesFit : IMicrocodeRule
    {
        public void Check(MicrocodeContext context)
        {
            if (context.OpCodes > DecoderRom.AddressSpace) context.Error(null, null, null, $"needs {context.OpCodes} opcodes but only {DecoderRom.AddressSpace} are available.");
        }
    }

    internal sealed class MnemonicIsOneUniqueWord : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction.Mnemonic) || instruction.Mnemonic.Any(char.IsWhiteSpace))
            {
                context.Error(instruction, null, null, "mnemonic must be a single word.");
            }
            else if (!context.Mnemonics.Add(instruction.Mnemonic))
            {
                context.Error(instruction, null, null, $"'{instruction.Mnemonic}' is defined more than once.");
            }
        }
    }

    internal sealed class HasSteps : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (instruction.Steps.Count == 0) context.Error(instruction, null, null, "has no steps.");
        }
    }

    internal sealed class OperandsAreNotNegative : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (instruction.Operands < 0) context.Error(instruction, null, null, "operands can not be negative.");
        }
    }

    internal sealed class OperandTypesMatchOperands : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (instruction.Operands == null || instruction.OperandTypes == null || instruction.OperandTypes.Count == instruction.Operands) return;
            context.Error(instruction, null, null,
                $"declares {Words.Count(instruction.Operands.Value, "operand")} but {Words.Count(instruction.OperandTypes.Count, "operand type")}.");
        }
    }

    // An instruction takes as many opcodes as its longest flag variant has steps, and at least one.
    internal sealed class TakesOpCodes : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            context.OpCodes += DecoderRom.BlockSize(instruction);
        }
    }

    internal sealed class EachStep : IInstructionRule
    {
        private readonly IStepRule[] rules;
        public EachStep(IStepRule[] rules) { this.rules = rules; }
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            for (int index = 0; index < instruction.Steps.Count; index++)
            {
                var step = new StepContext(context, instruction, index);
                foreach (var rule in rules) rule.Check(context, step);
            }
        }
    }

    // Every flag variant should end by returning to fetch (reset or load the micro step register) or by halting,
    // otherwise the micro step counter runs on into the next instruction's microcode.
    internal sealed class ReturnsToFetch : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (context.Ends == null || instruction.Steps.Count == 0) return;
            var reported = new HashSet<string>();
            for (int status = 0; status < DecoderRom.StatusVariants; status++)
            {
                var variant = instruction.StepsFor(status);
                var key = string.Join(",", variant.Select(s => instruction.Steps.IndexOf(s)));
                if (variant.Count > 0 && reported.Add(key)) Check(context, instruction, status, variant);
            }
        }

        private static void Check(MicrocodeContext context, InstructionDefinition instruction, int status, List<MicroStep> variant)
        {
            var flags = VariantLabel(instruction, status);
            int end = variant.FindIndex(context.Ends.Ends);
            if (end < 0)
            {
                context.Warning(instruction, instruction.Steps.IndexOf(variant[^1]), null,
                    $"{flags}never resets or loads '{context.Ends.StepRegister}', so it runs on into the next instruction's microcode.");
            }
            else if (end < variant.Count - 1)
            {
                context.Warning(instruction, instruction.Steps.IndexOf(variant[end + 1]), null,
                    $"{flags}step {instruction.Steps.IndexOf(variant[end]) + 1} returns to fetch, so the steps after it never run.");
            }
        }

        // "when C=1 Z=0: " for the flags the instruction's steps test, or nothing when none has a condition.
        private static string VariantLabel(InstructionDefinition instruction, int status)
        {
            if (instruction.Steps.All(s => s.When == null)) return "";
            int tested = instruction.Steps.Aggregate(0, (bits, s) => bits | (s.When?.Care ?? 0));
            var flags = FlagCondition.Flags.Where(f => (tested & f.Bit) != 0).Select(f => $"{f.Name}={((status & f.Bit) != 0 ? 1 : 0)}");
            return "when " + string.Join(" ", flags) + ": ";
        }
    }

    internal sealed class RegisterOperandsHaveARegisterFile : IInstructionRule
    {
        public void Check(MicrocodeContext context, InstructionDefinition instruction)
        {
            if (instruction.OperandTypes?.Contains(OperandType.Register) == true && RegisterFile.CountIn(context.Machine) == 0)
            {
                context.Error(instruction, null, null, "has a register operand, but the machine has no registerFile for it to name.");
            }
        }
    }

    internal sealed class NoRepeatedSignals : IStepRule
    {
        public void Check(MicrocodeContext context, StepContext step)
        {
            foreach (var repeated in step.Texts.GroupBy(s => s).Where(g => g.Count() > 1))
            {
                context.Warning(step.Instruction, step.Index, repeated.Key, $"'{repeated.Key}' is listed more than once.");
            }
        }
    }

    // Every signal must name a device and one of its control lines.
    internal sealed class SignalsExist : IStepRule
    {
        public void Check(MicrocodeContext context, StepContext step)
        {
            foreach (var signal in step.Resolved.Where(s => s.Problem != null)) context.Error(step.Instruction, step.Index, signal.Text, signal.Problem);
        }
    }

    // No two signals in a step may drive the same bus.
    internal sealed class OneDriverPerBus : IStepRule
    {
        public void Check(MicrocodeContext context, StepContext step)
        {
            foreach (var conflict in step.Resolved.Where(s => s.DrivenBus != null).GroupBy(s => s.DrivenBus).Where(g => g.Count() > 1))
            {
                var texts = conflict.Select(s => s.Text).ToList();
                context.Error(step.Instruction, step.Index, texts[1], $"{string.Join(" and ", texts)} all drive bus '{conflict.Key}' in the same tick.");
            }
        }
    }

    // A bus nothing drives reads 0, which is rarely what was meant.
    internal sealed class ReadBusesAreDriven : IStepRule
    {
        public void Check(MicrocodeContext context, StepContext step)
        {
            var driven = step.Resolved.Select(s => s.DrivenBus).Where(b => b != null).ToHashSet();
            foreach (var read in step.Resolved.Where(s => s.ReadBus != null && !driven.Contains(s.ReadBus)).GroupBy(s => s.ReadBus))
            {
                var texts = read.Select(s => s.Text).ToList();
                context.Warning(step.Instruction, step.Index, texts[0], $"{string.Join(" and ", texts)} read bus '{read.Key}', but nothing drives it in this step (reads 0).");
            }
        }
    }

    // A line that writes a connected device (an ALU operation writing its flags into the status register) and a line
    // of that device that changes it, in the same step: which write wins depends on the order of the devices.
    internal sealed class OneWriterOfTheFlags : IStepRule
    {
        public void Check(MicrocodeContext context, StepContext step)
        {
            foreach (var device in context.Machine.Devices)
            {
                foreach (var connection in WrittenConnections(context, device)) Check(context, step, device, connection);
            }
        }

        // The connections of the device that its lines write, and that are connected.
        private static IEnumerable<string> WrittenConnections(MicrocodeContext context, DeviceDefinition device)
        {
            var lines = context.Registry.Info(device.Type)?.ControlLines ?? new List<ControlLineInfo>();
            return lines.Select(l => l.WritesConnection).Where(c => c != null && device.Connections.ContainsKey(c)).Distinct();
        }

        private static void Check(MicrocodeContext context, StepContext step, DeviceDefinition device, string connection)
        {
            var target = device.Connections[connection];
            var operation = step.Resolved.FirstOrDefault(s => s.Signal.Device == device.Id && s.Line?.WritesConnection == connection);
            // Any line of the target but one that only puts its value on a bus changes it.
            var write = step.Resolved.FirstOrDefault(s => s.Signal.Device == target && s.Line != null && s.Line.Drives == null);
            if (operation == null || write == null) return;
            context.Error(step.Instruction, step.Index, write.Text,
                $"{operation.Text} writes the flags into {target}, and {write.Text} writes {target} too in the same step; only one can win. Move one of them to another step.");
        }
    }
}
