using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Hardware
{
    // The decoder: the devices its sockets name, and its ROM, the machine's microcode.
    public partial class DecoderInspector
    {
        [Parameter] public MachineDesign Design { get; set; }
        [Parameter] public EventCallback<(string Mnemonic, int Step)> OnOpenMicrocode { get; set; }

        private DecoderDefinition Decoder => Design.Definition.Decoder;
        private MicrocodeDefinition Microcode => Decoder.Microcode;

        // Any device can be the status register; the other sockets take a device of their type, or keep the one they
        // name now even if it is of another type.
        private IEnumerable<(string, string)> StatusRegisters => Choices(_ => true);
        private IEnumerable<(string, string)> InstructionRegisters => Choices(d => d.Type == "instructionRegister" || d.Id == Decoder.InstructionRegister);
        private IEnumerable<(string, string)> InterruptControllers => Choices(d => d.Type == "interruptController" || d.Id == Decoder.Interrupts);

        private IEnumerable<(string, string)> Choices(Func<DeviceDefinition, bool> fits)
        {
            return Design.Definition.Devices.Where(fits).Select(d => (d.Id, $"{d.Id} ({d.Type})"));
        }

        private Task OpenMicrocode() => OnOpenMicrocode.InvokeAsync((Microcode?.Fetch?.Mnemonic, 0));
    }
}
