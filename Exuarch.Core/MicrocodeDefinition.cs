using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Exuarch.Core
{
    // The microcode of a machine: a fetch routine at opcode 0 followed by the instruction set.
    // Each instruction is a list of micro steps; one step runs per clock tick.
    public class MicrocodeDefinition
    {
        public string Name { get; set; }
        public InstructionDefinition Fetch { get; set; }
        public List<InstructionDefinition> Instructions { get; set; } = new List<InstructionDefinition>();

        // Fetch first, then the instructions, in opcode order.
        [JsonIgnore]
        public IEnumerable<InstructionDefinition> AllInstructions
        {
            get
            {
                if (Fetch != null) yield return Fetch;
                foreach (var instruction in Instructions) yield return instruction;
            }
        }
        public InstructionDefinition FindInstruction(string mnemonic)
        {
            return AllInstructions.FirstOrDefault(i => i.Mnemonic == mnemonic);
        }

        public static MicrocodeDefinition FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.MicrocodeDefinition)
                       ?? throw new MachineDefinitionException("Microcode is empty.");
            }
            catch (JsonException e)
            {
                var location = e.Path == null ? "" : $" at {e.Path} (line {e.LineNumber + 1})";
                throw new MachineDefinitionException($"Microcode is not valid JSON{location}: {e.Message}");
            }
        }
        public string ToJson()
        {
            return CompactJson.Format(JsonSerializer.Serialize(this, MachineDefinitionJsonContext.Default.MicrocodeDefinition));
        }
        public MicrocodeDefinition Clone()
        {
            return FromJson(ToJson());
        }
        // Accepts microcode JSON, or the legacy tab separated format.
        public static MicrocodeDefinition Parse(string text)
        {
            return (text ?? "").TrimStart().StartsWith("{") ? FromJson(text) : FromTsv(text ?? "");
        }

        // Converts the legacy tab separated format: clock flag, device, function, mnemonic, N, V, C, Z.
        // A "p" row starts a new micro step, "s" rows add to the current step. Rows of one step must share
        // the same flag condition. The first mnemonic becomes the fetch routine.
        public static MicrocodeDefinition FromTsv(string tsv)
        {
            var microcode = new MicrocodeDefinition();
            var byMnemonic = new Dictionary<string, TsvInstruction>();
            int lineNumber = 0;
            foreach (var line in SourceText.SplitLines(tsv))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var row = TsvRow.Parse(line, lineNumber);
                if (!byMnemonic.TryGetValue(row.Mnemonic, out var instruction))
                {
                    byMnemonic[row.Mnemonic] = instruction = new TsvInstruction(row.Mnemonic);
                    microcode.Add(instruction.Definition);
                }
                instruction.Add(row);
            }
            return microcode;
        }
        // The first instruction added is the fetch routine.
        private void Add(InstructionDefinition instruction)
        {
            if (Fetch == null) Fetch = instruction;
            else Instructions.Add(instruction);
        }
    }

    public class InstructionDefinition
    {
        public string Mnemonic { get; set; }
        public string Description { get; set; }
        // Number of operand cells following the opcode. When set, the assembler checks it.
        public int? Operands { get; set; }
        // What each operand means: a value used as it is, or an address to read, write or jump to. Optional;
        // when given there is one per operand.
        public List<OperandType> OperandTypes { get; set; }
        public List<MicroStep> Steps { get; set; } = new List<MicroStep>();

        // The operand count, from Operands or else from OperandTypes.
        [JsonIgnore]
        public int? OperandCount { get { return Operands ?? OperandTypes?.Count; } }
        // The type of the operand at an index, or null when the instruction does not say.
        public OperandType? OperandTypeAt(int index)
        {
            return OperandTypes != null && index >= 0 && index < OperandTypes.Count ? OperandTypes[index] : null;
        }
        // "LDA address" style usage, or the operand count when the types are not given.
        [JsonIgnore]
        public string Signature
        {
            get
            {
                if (OperandTypes != null && OperandTypes.Count > 0) return Mnemonic + " " + string.Join(", ", OperandTypes.Select(OperandTypeNames.Name));
                return OperandCount switch
                {
                    null => Mnemonic,
                    0 => Mnemonic,
                    1 => Mnemonic + " operand",
                    var n => Mnemonic + " " + string.Join(", ", Enumerable.Range(1, n.Value).Select(i => $"operand{i}")),
                };
            }
        }

        // The steps that run when the status register holds the given flags, in order.
        public List<MicroStep> StepsFor(int status)
        {
            return Steps.Where(s => s.AppliesTo(status)).ToList();
        }
    }

    // The word used for an operand type in the editors and in JSON.
    public static class OperandTypeNames
    {
        public static string Name(OperandType type)
        {
            return type switch { OperandType.Address => "address", OperandType.Register => "register", _ => "value" };
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<OperandType>))]
    public enum OperandType
    {
        // Used as it is, for example the number to load.
        [JsonStringEnumMemberName("value")] Value,
        // A memory address to read, write or jump to.
        [JsonStringEnumMemberName("address")] Address,
        // A register of the machine's register file, written R0, R1 and so on; the cell holds its number.
        [JsonStringEnumMemberName("register")] Register,
    }

    public class MicroStep
    {
        // Only run this step when the flags match. Null means always.
        public FlagCondition When { get; set; }
        // Control lines to enable, as "device.line".
        public List<string> Signals { get; set; } = new List<string>();
        public string Comment { get; set; }

        public bool AppliesTo(int status)
        {
            return When == null || When.Matches(status);
        }
    }

    // A condition on the decoder status. It is kept as two masks: the status bits it tests, and the values those
    // bits must have. The JSON and the editors see one bool? per flag, null meaning either.
    public class FlagCondition
    {
        // Bit 4 of the decoder status: the four flags are bits 0 to 3.
        public const int InterruptBit = 0x10;

        // The flags in the order patterns and labels list them.
        internal static readonly (string Name, int Bit)[] Flags =
        {
            ("N", StatusRegister.NegativeFlag), ("V", StatusRegister.OverflowFlag), ("C", StatusRegister.CarryFlag),
            ("Z", StatusRegister.ZeroFlag), ("I", InterruptBit),
        };

        private int care;
        private int wanted;

        [JsonPropertyName("N")] public bool? N { get { return Get(StatusRegister.NegativeFlag); } set { Set(StatusRegister.NegativeFlag, value); } }
        [JsonPropertyName("V")] public bool? V { get { return Get(StatusRegister.OverflowFlag); } set { Set(StatusRegister.OverflowFlag, value); } }
        [JsonPropertyName("C")] public bool? C { get { return Get(StatusRegister.CarryFlag); } set { Set(StatusRegister.CarryFlag, value); } }
        [JsonPropertyName("Z")] public bool? Z { get { return Get(StatusRegister.ZeroFlag); } set { Set(StatusRegister.ZeroFlag, value); } }
        // An interrupt request, from the machine's interrupt controller (decoder.interrupts).
        [JsonPropertyName("I")] public bool? I { get { return Get(InterruptBit); } set { Set(InterruptBit, value); } }

        // The status bits the condition tests.
        internal int Care { get { return care; } }

        private bool? Get(int bit)
        {
            return (care & bit) == 0 ? null : (wanted & bit) != 0;
        }
        private void Set(int bit, bool? required)
        {
            care = required == null ? care & ~bit : care | bit;
            wanted = required == true ? wanted | bit : wanted & ~bit;
        }

        [JsonIgnore]
        public bool IsAlways { get { return care == 0; } }

        public bool Matches(int status)
        {
            return (status & care) == wanted;
        }

        // Pattern of characters for N, V, C, Z and I, each 0, 1 or x. A four character pattern leaves I out.
        // Returns null when every condition is x.
        public static FlagCondition FromPattern(string pattern)
        {
            var condition = new FlagCondition();
            for (int i = 0; i < Math.Min(pattern.Length, Flags.Length); i++)
            {
                if (pattern[i] != 'x') condition.Set(Flags[i].Bit, pattern[i] == '1');
            }
            return condition.IsAlways ? null : condition;
        }
        public static string ToPattern(FlagCondition condition)
        {
            var pattern = new char[Flags.Length];
            for (int i = 0; i < Flags.Length; i++) pattern[i] = Symbol(condition?.Get(Flags[i].Bit), 'x');
            return new string(pattern);
        }
        public static bool AreEqual(FlagCondition a, FlagCondition b)
        {
            return ToPattern(a) == ToPattern(b);
        }
        public override string ToString()
        {
            var parts = Flags.Where(f => (care & f.Bit) != 0).Select(f => $"{f.Name}={Symbol(Get(f.Bit), ' ')}").ToList();
            return parts.Count == 0 ? "always" : string.Join(" ", parts);
        }
        // A flag's value as 0 or 1, or the given symbol when it is not tested.
        private static char Symbol(bool? flag, char either)
        {
            return flag == null ? either : flag.Value ? '1' : '0';
        }
    }

    // A control line reference, "device.line".
    public readonly struct Signal
    {
        public readonly string Device;
        public readonly string Line;
        public Signal(string device, string line)
        {
            Device = device;
            Line = line;
        }
        public static bool TryParse(string text, out Signal signal)
        {
            signal = default;
            if (text == null) return false;
            var dot = text.IndexOf('.');
            if (dot <= 0 || dot == text.Length - 1 || text.IndexOf('.', dot + 1) >= 0 || text.Any(char.IsWhiteSpace)) return false;
            signal = new Signal(text.Substring(0, dot), text.Substring(dot + 1));
            return true;
        }
        public override string ToString() { return $"{Device}.{Line}"; }
    }
}
