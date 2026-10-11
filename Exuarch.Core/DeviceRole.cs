using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    internal sealed class DeviceRole
    {
        public static readonly DeviceRole Status = new DeviceRole("decoder.status", required: true, d => d.Decoder?.Status, (d, id) => InDecoder(d, decoder => decoder.Status = id));
        public static readonly DeviceRole InstructionRegister = new DeviceRole("decoder.instructionRegister", required: true,
            d => d.Decoder?.InstructionRegister, (d, id) => InDecoder(d, decoder => decoder.InstructionRegister = id), "reset", "load");
        public static readonly DeviceRole Interrupts = new DeviceRole("decoder.interrupts", required: false, d => d.Decoder?.Interrupts, (d, id) => InDecoder(d, decoder => decoder.Interrupts = id));
        public static readonly DeviceRole Halt = new DeviceRole("halt", required: false, d => d.Halt, (d, id) => d.Halt = id, "disable");
        public static readonly DeviceRole ProgramMemory = new DeviceRole("programMemory", required: false, d => d.ProgramMemory, (d, id) => d.ProgramMemory = id);

        public static readonly IReadOnlyList<DeviceRole> All = new[] { Status, InstructionRegister, Interrupts, Halt, ProgramMemory };

        private readonly Func<MachineDefinition, string> get;
        private readonly Action<MachineDefinition, string> set;

        private DeviceRole(string setting, bool required, Func<MachineDefinition, string> get, Action<MachineDefinition, string> set, params string[] endingLines)
        {
            Setting = setting;
            Required = required;
            this.get = get;
            this.set = set;
            EndingLines = endingLines;
        }

        public string Setting { get; }
        public bool Required { get; }
        public bool OnDecoder { get { return Setting.StartsWith("decoder."); } }
        public IReadOnlyList<string> EndingLines { get; }

        private static void InDecoder(MachineDefinition definition, Action<DecoderDefinition> set)
        {
            if (definition.Decoder != null) set(definition.Decoder);
        }

        public string DeviceIn(MachineDefinition definition) { return get(definition); }
        public void Assign(MachineDefinition definition, string id) { set(definition, id); }
    }
}
