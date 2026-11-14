using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public sealed record ResolvedSignal(string Text, Signal Signal, DeviceDefinition Device, ControlLineInfo Line, string DrivenBus, string ReadBus, string Problem)
    {
        public static ResolvedSignal Invalid(string text, string problem) { return new ResolvedSignal(text, default, null, null, null, null, problem); }
    }

    public sealed class SignalResolver
    {
        private readonly MachineDefinition machine;
        private readonly DeviceRegistry registry;
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
            return OnLine(text, signal, device, info, info?.ControlLines.FirstOrDefault(l => l.Name == signal.Line));
        }

        public static List<string> LinesOf(DeviceTypeInfo info, IBusDevice built)
        {
            return info?.ControlLines.Count > 0 ? info.ControlLines.Select(l => l.Name).ToList() : built?.SignalLines();
        }

        private static ResolvedSignal OnLine(string text, Signal signal, DeviceDefinition device, DeviceTypeInfo info, ControlLineInfo line)
        {
            string driven = PortBus(device, line?.Drives), read = PortBus(device, line?.Reads);
            return new ResolvedSignal(text, signal, device, line, driven, read, UnconnectedOptionalPort(text, device, info, line));
        }

        private static string UnconnectedOptionalPort(string text, DeviceDefinition device, DeviceTypeInfo info, ControlLineInfo line)
        {
            string port = line?.Drives ?? line?.Reads;
            if (port == null || info == null || !info.OptionalPorts.Contains(port) || device.GetPortBus(port) != null) return null;
            return $"'{text}' uses port '{port}' of '{device.Id}', which is not connected to a bus.";
        }

        private static string PortBus(DeviceDefinition device, string port)
        {
            return port == null ? null : device.GetPortBus(port);
        }
    }
}
