using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // What the drawing's wires show this tick: every bus, lit when something drives it; every port's wire, as driving,
    // reading or idle; the connections between devices and from the decoder; and the value on each bus that carries one.
    public sealed class LiveWiring
    {
        private readonly RunSession session;
        private readonly SchematicLayout layout;
        private readonly MachineDefinition definition;
        private readonly LastTickView last;

        public LiveWiring(RunSession session, SchematicLayout layout)
        {
            this.session = session;
            this.layout = layout;
            definition = session.Machine.Definition;
            last = session.Last;
        }

        public IEnumerable<BusBar> Buses => definition.Buses.Where(b => b.Layout != null)
            .Select(bus => new BusBar(bus.Id, bus.Layout.Y, session.BusColor(bus.Id), last.TransferOn(bus.Id) != null ? "bus-bar live" : "bus-bar "));

        // Each device's ports, then its connections, in the order the devices are defined.
        public IEnumerable<DeviceWires> Devices => definition.Devices.Where(d => d.Layout != null)
            .Select(device => new DeviceWires(PortsOf(device).ToList(), ConnectionsOf(device).ToList()));

        public IEnumerable<string> DecoderConnections
        {
            get
            {
                if (definition.Decoder?.Layout == null) return Enumerable.Empty<string>();
                return SchematicLayout.DecoderSockets.Select((socket, i) => (Index: i, Target: definition.FindDevice(layout.DecoderTarget(socket.Name) ?? "")))
                    .Where(socket => socket.Target?.Layout != null)
                    .Select(socket => layout.DecoderPath(socket.Index, socket.Target));
            }
        }

        // The value on every bus a device on the drawing drives, over the port it drives it from.
        public IEnumerable<BusValue> Values => definition.Buses.Select(ValueOn).Where(value => value != null);

        private IEnumerable<PortWire> PortsOf(DeviceDefinition device)
        {
            foreach (var port in device.Ports())
            {
                if (definition.FindBus(port.Value)?.Layout == null) continue;
                var anchor = layout.PortAnchor(device, port.Key);
                yield return PortWire.Of(anchor.X, anchor.Y, layout.BusY(port.Value), session.BusColor(port.Value), Flow(device.Id, port.Value));
            }
        }

        private string Flow(string deviceId, string busId)
        {
            if (last.Drives(deviceId, busId)) return "drive";
            return last.Reads(deviceId, busId) ? "read" : "idle";
        }

        private IEnumerable<string> ConnectionsOf(DeviceDefinition device)
        {
            var connections = session.Info(device).Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                if (!device.Connections.TryGetValue(connections[i].Name, out var targetId)) continue;
                var target = definition.FindDevice(targetId);
                if (target?.Layout != null) yield return layout.ConnectionPath(device, i, target);
            }
        }

        private BusValue ValueOn(BusDefinition bus)
        {
            var transfer = last.TransferOn(bus.Id);
            if (transfer == null) return null;
            var driver = definition.FindDevice(transfer.Driver ?? "");
            if (driver?.Layout == null || bus.Layout == null) return null;
            var port = driver.Ports().First(p => p.Value == bus.Id).Key;
            return new BusValue($"{last.Cycle}:{bus.Id}", layout.PortAnchor(driver, port).X, bus.Layout.Y, session.BusColor(bus.Id), Components.Formats.Hex(transfer.Value));
        }
    }

    public sealed record BusBar(string Id, double Y, string Color, string Css);

    public sealed record DeviceWires(List<PortWire> Ports, List<string> Connections);

    // A port's wire to its bus: drawn from the driver to the bus, or from the bus to a reader, in the bus's colour with an
    // arrow head; idle, a plain line. The tap is the dot where it meets the bus.
    public sealed record PortWire(string Css, double X, double Y1, double Y2, string Stroke, string Marker, double TapY, string Color)
    {
        private const string Arrow = "url(#run-arrow)";

        public static PortWire Of(double x, double portY, double busY, string color, string flow) => flow switch
        {
            "drive" => new PortWire("wire drive", x, portY, busY, color, Arrow, busY, color),
            "read" => new PortWire("wire read", x, busY, portY, color, Arrow, busY, color),
            _ => new PortWire("wire idle", x, portY, busY, null, null, busY, color),
        };
    }

    public sealed record BusValue(string Key, double X, double Y, string Color, string Text);
}
