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

        // A socket offers the devices that fit it, and keeps the one it names now even if that does not fit.
        private IEnumerable<(string, string)> StatusRegisters => Fitting("status", Decoder.Status);
        private IEnumerable<(string, string)> InstructionRegisters => Fitting("instructionRegister", Decoder.InstructionRegister);
        private IEnumerable<(string, string)> InterruptControllers => Fitting("interrupts", Decoder.Interrupts);

        private IEnumerable<(string, string)> Fitting(string socket, string current)
        {
            return Choices(d => MachineDesign.FitsDecoderSocket(socket, d) || d.Id == current);
        }

        private IEnumerable<(string, string)> Choices(Func<DeviceDefinition, bool> fits)
        {
            return Design.Definition.Devices.Where(fits).Select(d => (d.Id, $"{d.Id} ({d.Type})"));
        }

        private Task OpenMicrocode() => OnOpenMicrocode.InvokeAsync((Microcode?.Fetch?.Mnemonic, 0));
    }
}
