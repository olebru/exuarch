using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Microcoding
{
    // What the machine says about the signals microcode can turn on: each device's control lines, the bus a line drives
    // or reads, and the colour of the device's category. The machine may be missing, and then there are none.
    public sealed class SignalCatalog
    {
        private readonly MachineDefinition machine;
        private readonly DeviceRegistry registry;
        // Looks a signal up every time it is asked, since the machine can change while the microcode is edited.
        private readonly SignalResolver resolver;

        public SignalCatalog(MachineDefinition machine, DeviceRegistry registry)
        {
            this.machine = machine;
            this.registry = registry;
            resolver = machine == null ? null : new SignalResolver(machine, registry);
        }

        // The signal looked up in the machine, or null without one.
        private ResolvedSignal Find(string signalText) => resolver?.Look(signalText);

        public ControlLineInfo LineFor(string signalText) => Find(signalText)?.Line;

        public string Color(string signalText)
        {
            if (!Signal.TryParse(signalText, out var signal)) return Palette.Error;
            var device = machine?.FindDevice(signal.Device);
            return Palette.Category(device == null ? null : registry.Info(device.Type));
        }

        // Every signal of every device, for the suggestions of the field that adds one.
        public IEnumerable<string> All()
        {
            return Lines(null).SelectMany(group => group.Lines.Select(line => $"{group.Device.Id}.{line.Name}"));
        }

        // Each device with control lines, and those of its lines whose signal contains the filter (all of them for none).
        public IEnumerable<(DeviceDefinition Device, DeviceTypeInfo Info, List<ControlLineInfo> Lines)> Lines(string filter)
        {
            if (machine == null) yield break;
            foreach (var device in machine.Devices)
            {
                var info = registry.Info(device.Type);
                if (info == null) continue;
                var lines = info.ControlLines.Where(l => string.IsNullOrEmpty(filter) || $"{device.Id}.{l.Name}".Contains(filter, System.StringComparison.OrdinalIgnoreCase)).ToList();
                if (lines.Count > 0) yield return (device, info, lines);
            }
        }

        // A signal in a step: what it does, and how to find its device.
        public string Title(string signalText)
        {
            var signal = Find(signalText);
            return signal?.Line == null ? signalText : Description(signal) + " (double-click to show the device)";
        }

        // A signal in the palette: what it does.
        public string PaletteTitle(string signalText)
        {
            var signal = Find(signalText);
            return signal?.Line == null ? signalText : Description(signal);
        }

        private static string Description(ResolvedSignal signal)
        {
            var line = signal.Line;
            var bus = "";
            if (line.Drives != null) bus = $" · drives bus {signal.DrivenBus ?? "(not connected)"}";
            if (line.Reads != null) bus = $" · reads bus {signal.ReadBus ?? "(not connected)"}";
            return $"{signal.Text}: {line.Description}{bus}";
        }

        public static string DirectionMark(ControlLineInfo line)
        {
            return line?.Drives != null ? "▲" : line?.Reads != null ? "▼" : "";
        }

        // For each bus touched in the step: the devices that drive it and those that read it.
        public List<BusFlow> BusActivity(MicroStep step)
        {
            var activity = new Dictionary<string, BusFlow>();
            foreach (var text in step.Signals)
            {
                var signal = Find(text);
                if (signal?.Line == null) continue;
                Note(activity, signal.Device.Id, signal.DrivenBus, true);
                Note(activity, signal.Device.Id, signal.ReadBus, false);
            }
            return activity.Values.ToList();
        }

        private static void Note(Dictionary<string, BusFlow> activity, string deviceId, string busId, bool drives)
        {
            if (busId == null) return;
            if (!activity.TryGetValue(busId, out var transfer)) activity[busId] = transfer = new BusFlow(busId, new List<string>(), new List<string>());
            (drives ? transfer.Drivers : transfer.Readers).Add(deviceId);
        }
    }

    // What happens on one bus in a step: who puts a value on it and who takes it.
    public sealed record BusFlow(string Bus, List<string> Drivers, List<string> Readers)
    {
        // Two devices driving at once is a conflict; readers with nothing driving read a floating bus.
        public string State => Drivers.Count > 1 ? "conflict" : Drivers.Count == 0 ? "floating" : "";
        public string From => Drivers.Count == 0 ? "nothing" : string.Join(" + ", Drivers);
        public string To => Readers.Count == 0 ? "—" : string.Join(", ", Readers);
    }
}
