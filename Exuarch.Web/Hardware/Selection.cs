using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Hardware
{
    // What the hardware design has selected, and so what the inspector shows: nothing (the machine itself), a device, a
    // bus or the decoder.
    public abstract record Selection
    {
        public static readonly Selection None = new NoSelection();
        public static readonly Selection Decoder = new DecoderSelection();

        // The same selection in another definition, or none when what it names is not in that one.
        public virtual Selection Revalidate(MachineDefinition definition) => this;

        public virtual bool IsBus(string id) => false;
        public virtual bool IsDevice(string id) => false;
        public virtual bool IsDecoder => false;

        // What a validation problem is about, to select it from the list of problems: the device it names, or the
        // decoder; null when it is about neither.
        public static Selection ForProblem(MachineDefinition definition, string error)
        {
            var device = definition.Devices.Select(d => d.Id).FirstOrDefault(id => error.Contains($"'{id}'"));
            if (device != null) return new DeviceSelection(device);
            return error.Contains("\"decoder") && definition.Decoder != null ? Decoder : null;
        }
    }

    public sealed record NoSelection : Selection;

    public sealed record DecoderSelection : Selection
    {
        public override bool IsDecoder => true;
    }

    public sealed record DeviceSelection(string Id) : Selection
    {
        public override Selection Revalidate(MachineDefinition definition) => definition?.FindDevice(Id) == null ? None : this;
        public override bool IsDevice(string id) => id == Id;
    }

    public sealed record BusSelection(string Id) : Selection
    {
        public override Selection Revalidate(MachineDefinition definition) => definition?.FindBus(Id) == null ? None : this;
        public override bool IsBus(string id) => id == Id;
    }
}
