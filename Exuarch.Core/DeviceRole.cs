using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A device the machine as a whole names for a part of its own: the decoder's status register, micro step
    // register and interrupt controller, the halt clock and the program memory. Checking a definition, building the
    // machine, and renaming or removing a device all go through this table.
    internal sealed class DeviceRole
    {
        public static readonly DeviceRole Status = new DeviceRole("decoder.status", required: true, d => d.Decoder?.Status, (d, id) => InDecoder(d, decoder => decoder.Status = id));
        public static readonly DeviceRole InstructionRegister = new DeviceRole("decoder.instructionRegister", required: true,
            d => d.Decoder?.InstructionRegister, (d, id) => InDecoder(d, decoder => decoder.InstructionRegister = id), "reset", "load");
        public static readonly DeviceRole Interrupts = new DeviceRole("decoder.interrupts", required: false, d => d.Decoder?.Interrupts, (d, id) => InDecoder(d, decoder => decoder.Interrupts = id));
        public static readonly DeviceRole Halt = new DeviceRole("halt", required: false, d => d.Halt, (d, id) => d.Halt = id, "disable");
        public static readonly DeviceRole ProgramMemory = new DeviceRole("programMemory", required: false, d => d.ProgramMemory, (d, id) => d.ProgramMemory = id);

        // In the order a definition's problems with them are listed.
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

        // Where the definition names the device, as messages quote it.
        public string Setting { get; }
        public bool Required { get; }
        // Set in the decoder, so only there when the definition has one.
        public bool OnDecoder { get { return Setting.StartsWith("decoder."); } }
        // The device's lines that end an instruction: the micro step register's jumps back to fetch, and the halt
        // clock's stop.
        public IReadOnlyList<string> EndingLines { get; }

        // A machine without a decoder has no decoder roles to give.
        private static void InDecoder(MachineDefinition definition, Action<DecoderDefinition> set)
        {
            if (definition.Decoder != null) set(definition.Decoder);
        }

        // The ID of the device in this role, or null.
        public string DeviceIn(MachineDefinition definition) { return get(definition); }
        // Gives the role to another device, or to none.
        public void Assign(MachineDefinition definition, string id) { set(definition, id); }
    }
}
