using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Exuarch.Core
{
    public class MicrocodeDefinition
    {
        private int? formatVersionInFile;
        [JsonPropertyName("formatVersion"), JsonPropertyOrder(-1)]
        public int? FormatVersionJson { get => null; set => formatVersionInFile = value; }
        public string Name { get; set; }
        public int? OpcodeBits { get; set; }
        public List<FieldDefinition> Fields { get; set; }
        public InstructionDefinition Fetch { get; set; }
        public List<InstructionDefinition> Instructions { get; set; } = new List<InstructionDefinition>();

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
                var microcode = JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.MicrocodeDefinition)
                                ?? throw new MachineDefinitionException("Microcode is empty.");
                microcode.AcceptFileFormat();
                return microcode;
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
        public string ToFileJson()
        {
            return FileFormat.WithCurrentVersion(ToJson());
        }
        public void AcceptFileFormat()
        {
            FileFormat.RejectUnreadable(formatVersionInFile, Name ?? "This microcode");
            formatVersionInFile = null;
        }
        public MicrocodeDefinition Clone()
        {
            return FromJson(ToJson());
        }
        public static MicrocodeDefinition Parse(string text)
        {
            return (text ?? "").TrimStart().StartsWith("{") ? FromJson(text) : FromTsv(text ?? "");
        }

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
        public int? Operands { get; set; }
        public List<OperandType> OperandTypes { get; set; }
        public List<int> Fields { get; set; }
        public List<MicroStep> Steps { get; set; } = new List<MicroStep>();

        [JsonIgnore]
        public int? OperandCount { get { return Operands ?? OperandTypes?.Count; } }
        public OperandType? OperandTypeAt(int index)
        {
            return OperandTypes != null && index >= 0 && index < OperandTypes.Count ? OperandTypes[index] : null;
        }
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

        public List<MicroStep> StepsFor(int status)
        {
            return Steps.Where(s => s.AppliesTo(status)).ToList();
        }
    }

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
        [JsonStringEnumMemberName("value")] Value,
        [JsonStringEnumMemberName("address")] Address,
        [JsonStringEnumMemberName("register")] Register,
    }

    public class MicroStep
    {
        public FlagCondition When { get; set; }
        public List<string> Signals { get; set; } = new List<string>();
        public string Comment { get; set; }

        public bool AppliesTo(int status)
        {
            return When == null || When.Matches(status);
        }
    }

    public class FlagCondition
    {
        public const int InterruptBit = 0x10;

        internal static readonly (string Name, int Bit)[] Flags =
        {
            ("N", StatusRegister.NegativeFlag), ("V", StatusRegister.OverflowFlag), ("C", StatusRegister.CarryFlag),
            ("Z", StatusRegister.ZeroFlag), ("I", InterruptBit),
        };

        private int testedBits;
        private int expectedBits;

        [JsonPropertyName("N")] public bool? N { get { return Get(StatusRegister.NegativeFlag); } set { Set(StatusRegister.NegativeFlag, value); } }
        [JsonPropertyName("V")] public bool? V { get { return Get(StatusRegister.OverflowFlag); } set { Set(StatusRegister.OverflowFlag, value); } }
        [JsonPropertyName("C")] public bool? C { get { return Get(StatusRegister.CarryFlag); } set { Set(StatusRegister.CarryFlag, value); } }
        [JsonPropertyName("Z")] public bool? Z { get { return Get(StatusRegister.ZeroFlag); } set { Set(StatusRegister.ZeroFlag, value); } }
        [JsonPropertyName("I")] public bool? I { get { return Get(InterruptBit); } set { Set(InterruptBit, value); } }

        internal int TestedBits { get { return testedBits; } }

        private bool? Get(int bit)
        {
            return (testedBits & bit) == 0 ? null : (expectedBits & bit) != 0;
        }
        private void Set(int bit, bool? required)
        {
            testedBits = required == null ? testedBits & ~bit : testedBits | bit;
            expectedBits = required == true ? expectedBits | bit : expectedBits & ~bit;
        }

        [JsonIgnore]
        public bool IsAlways { get { return testedBits == 0; } }

        public bool Matches(int status)
        {
            return (status & testedBits) == expectedBits;
        }

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
            var parts = Flags.Where(f => (testedBits & f.Bit) != 0).Select(f => $"{f.Name}={Symbol(Get(f.Bit), ' ')}").ToList();
            return parts.Count == 0 ? "always" : string.Join(" ", parts);
        }
        private static char Symbol(bool? flag, char either)
        {
            return flag == null ? either : flag.Value ? '1' : '0';
        }
    }

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
