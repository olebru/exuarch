using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // A signal of the microcode looked up in the machine: the device and the control line it names, and the buses
    // that line drives and reads there. Problem says why it is not a signal of the machine, and is null when it is.
    // A device type that does not describe its lines leaves Line and the buses null.
    public sealed record ResolvedSignal(string Text, Signal Signal, DeviceDefinition Device, ControlLineInfo Line, string DrivenBus, string ReadBus, string Problem)
    {
        public static ResolvedSignal Invalid(string text, string problem) { return new ResolvedSignal(text, default, null, null, null, null, problem); }
    }

    // Looks signals up in a machine: Resolve looks each one up once, for checking microcode against a machine that does
    // not change meanwhile; Look looks it up every time, for one that can.
    public sealed class SignalResolver
    {
        private readonly MachineDefinition machine;
        private readonly DeviceRegistry registry;
        // Lists the lines of a device whose type does not describe them, when given.
        private readonly Machine built;
        private readonly Dictionary<string, ResolvedSignal> resolved = new Dictionary<string, ResolvedSignal>();

        public SignalResolver(MachineDefinition machine, DeviceRegistry registry, Machine built = null)
        {
            this.machine = machine;
            this.registry = registry;
            this.built = built;
        }

        public ResolvedSignal Resolve(string text)
        {
            if (!resolved.TryGetValue(text, out var signal)) resolved[text] = signal = Look(text);
            return signal;
        }

        public ResolvedSignal Look(string text)
        {
            if (!Signal.TryParse(text, out var signal)) return ResolvedSignal.Invalid(text, $"'{text}' is not a signal, write it as device.line.");
            var device = machine.FindDevice(signal.Device);
            if (device == null) return ResolvedSignal.Invalid(text, $"unknown device '{signal.Device}'.");
            var info = registry.Info(device.Type);
            var lines = LinesOf(info, built?.Device(device.Id));
            if (lines == null) return new ResolvedSignal(text, signal, device, null, null, null, null);
            if (!lines.Contains(signal.Line))
            {
                return ResolvedSignal.Invalid(text, $"device '{device.Id}' has no control line '{signal.Line}', it has {string.Join(", ", lines)}.");
            }
            return OnLine(text, signal, device, info?.ControlLines.FirstOrDefault(l => l.Name == signal.Line));
        }

        // The lines the device type describes, or else those of the built device; null when neither is known.
        public static List<string> LinesOf(DeviceTypeInfo info, IBusDevice built)
        {
            return info?.ControlLines.Count > 0 ? info.ControlLines.Select(l => l.Name).ToList() : built?.SignalLines();
        }

        private static ResolvedSignal OnLine(string text, Signal signal, DeviceDefinition device, ControlLineInfo line)
        {
            return new ResolvedSignal(text, signal, device, line, PortBus(device, line?.Drives), PortBus(device, line?.Reads), null);
        }

        private static string PortBus(DeviceDefinition device, string port)
        {
            return port == null ? null : device.GetPortBus(port);
        }
    }
}
