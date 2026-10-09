using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Exuarch.Core
{
    public class MachineDefinition
    {
        public string Name { get; set; }
        public List<BusDefinition> Buses { get; set; } = new List<BusDefinition>();
        public List<DeviceDefinition> Devices { get; set; } = new List<DeviceDefinition>();
        public DecoderDefinition Decoder { get; set; }
        // ID of the clock device whose "disable" line halts the machine. Optional.
        public string Halt { get; set; }
        // ID of the memory device the assembled program is loaded into. Required when a program is given.
        public string ProgramMemory { get; set; }

        public static MachineDefinition FromJson(string json)
        {
            try
            {
                var definition = JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.MachineDefinition)
                                 ?? throw new MachineDefinitionException("Machine definition is empty.");
                definition.DropRemovedParameters();
                return definition;
            }
            catch (JsonException e)
            {
                var location = e.Path == null ? "" : $" at {e.Path} (line {e.LineNumber + 1})";
                throw new MachineDefinitionException($"Machine definition is not valid JSON{location}: {e.Message}");
            }
        }
        public string ToJson()
        {
            return CompactJson.Format(JsonSerializer.Serialize(this, MachineDefinitionJsonContext.Default.MachineDefinition));
        }

        // Parameters that devices once had. Files that still set them open as if they did not: registers used to
        // take an initialValue, and now always start at 0.
        private static readonly (string Type, string Parameter)[] RemovedParameters =
        {
            ("register", "initialValue"), ("dualPortRegister", "initialValue"),
        };

        public void DropRemovedParameters()
        {
            foreach (var device in Devices)
            {
                foreach (var (type, parameter) in RemovedParameters)
                {
                    if (device.Type == type) device.Parameters.Remove(parameter);
                }
            }
        }
    }
    // Where an editor drew an element. Has no effect on the machine.
    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
    public class BusDefinition
    {
        public string Id { get; set; }
        public Position Layout { get; set; }
    }
    public class DeviceDefinition
    {
        public string Id { get; set; }
        public string Type { get; set; }
        // Display name, defaults to the ID.
        public string Name { get; set; }
        // Shorthand for a device with a single bus connection: connects port "data" to this bus.
        public string Bus { get; set; }
        // Port name to bus ID, for devices with more than one bus connection.
        [JsonIgnore]
        public Dictionary<string, string> Buses { get; set; } = new Dictionary<string, string>();
        // Named references to other devices, for example the ALU operand registers.
        [JsonIgnore]
        public Dictionary<string, string> Connections { get; set; } = new Dictionary<string, string>();
        [JsonIgnore]
        public Dictionary<string, JsonElement> Parameters { get; set; } = new Dictionary<string, JsonElement>();
        [JsonPropertyOrder(4)]
        public List<string> Contents { get; set; }
        [JsonPropertyOrder(5)]
        public Position Layout { get; set; }

        // Serialized forms of the collections above, left out of the JSON when empty.
        [JsonPropertyName("buses"), JsonPropertyOrder(1)]
        public Dictionary<string, string> BusesJson { get => Buses.Count == 0 ? null : Buses; set => Buses = value ?? new Dictionary<string, string>(); }
        [JsonPropertyName("connections"), JsonPropertyOrder(2)]
        public Dictionary<string, string> ConnectionsJson { get => Connections.Count == 0 ? null : Connections; set => Connections = value ?? new Dictionary<string, string>(); }
        [JsonPropertyName("parameters"), JsonPropertyOrder(3)]
        public Dictionary<string, JsonElement> ParametersJson { get => Parameters.Count == 0 ? null : Parameters; set => Parameters = value ?? new Dictionary<string, JsonElement>(); }

        public IEnumerable<KeyValuePair<string, string>> Ports()
        {
            if (!string.IsNullOrEmpty(Bus)) yield return new KeyValuePair<string, string>(DeviceBuildContext.DefaultPort, Bus);
            foreach (var port in Buses) yield return port;
        }
        public string GetPortBus(string port)
        {
            if (port == DeviceBuildContext.DefaultPort && !string.IsNullOrEmpty(Bus)) return Bus;
            return Buses.TryGetValue(port, out var busId) ? busId : null;
        }
        // Connects a port to a bus, or disconnects it when busId is null. Port "data" uses the "bus" shorthand.
        public void SetPortBus(string port, string busId)
        {
            if (port == DeviceBuildContext.DefaultPort)
            {
                Bus = busId;
                Buses.Remove(port);
            }
            else if (busId == null)
            {
                Buses.Remove(port);
            }
            else
            {
                Buses[port] = busId;
            }
        }
    }
    public class DecoderDefinition
    {
        // Register whose low 4 bits (NVCZ) select the status variant of a micro instruction.
        public string Status { get; set; }
        // The micro step counter: holds the decoder ROM step address that runs next.
        public string InstructionRegister { get; set; }
        // Optional interrupt controller: its request is the I condition microcode steps can test.
        public string Interrupts { get; set; }
        // Machine files from before the rename call it "instruction"; read it, never write it.
        [JsonPropertyName("instruction")]
        public string LegacyInstruction
        {
            get { return null; }
            set { if (value != null) InstructionRegister ??= value; }
        }
        // The decoder ROM contents: the fetch routine and the instruction set, driving this machine's control lines.
        public MicrocodeDefinition Microcode { get; set; }
        // Where the hardware design draws the decoder. Has no effect on the machine.
        public Position Layout { get; set; }
    }
    public class MachineDefinitionException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public MachineDefinitionException(string error) : this(new[] { error }) { }
        public MachineDefinitionException(IReadOnlyList<string> errors)
            : base(errors.Count == 1 ? errors[0] : "Machine definition has errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors))
        {
            Errors = errors;
        }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
    [JsonSerializable(typeof(MachineDefinition))]
    [JsonSerializable(typeof(MicrocodeDefinition))]
    [JsonSerializable(typeof(OperandType))]
    [JsonSerializable(typeof(MachinePackage))]
    [JsonSerializable(typeof(PackageManifest))]
    [JsonSerializable(typeof(Workspace))]
    [JsonSerializable(typeof(string))]
    internal partial class MachineDefinitionJsonContext : JsonSerializerContext
    {
    }
}
