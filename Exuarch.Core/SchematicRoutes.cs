using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Exuarch.Core
{
    internal readonly record struct WireId(NodeId Node, int Index);

    internal sealed record WireRequest(WireId Id, WireRouter.Wire Wire)
    {
        public static List<WireRequest> From(IReadOnlyList<SchematicNode> placed)
        {
            var indexOf = placed.Select((n, i) => (n.Id, i)).ToDictionary(n => n.Id, n => n.i);
            var requests = new List<WireRequest>();
            foreach (var node in placed)
            {
                var targets = node.SocketTargets;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] == null || !indexOf.TryGetValue(NodeId.Device(targets[i]), out var target)) continue;
                    requests.Add(new WireRequest(new WireId(node.Id, i), new WireRouter.Wire { Start = node.SocketPoint(i), Target = target }));
                }
            }
            return requests;
        }
    }

    internal static class ArrivalSpread
    {
        private const double Apart = 14;

        public static void Apply(IEnumerable<WireRouter.Wire> wires)
        {
            foreach (var group in wires.GroupBy(w => w.Target))
            {
                var ordered = group.OrderBy(w => w.Start.Y).ThenBy(w => w.Start.X).ToList();
                for (int k = 0; k < ordered.Count; k++) ordered[k].Spread = (k - (ordered.Count - 1) / 2.0) * Apart;
            }
        }
    }

    internal sealed class ValueList<T> : IEquatable<ValueList<T>>
    {
        private readonly List<T> items;

        public ValueList(List<T> items)
        {
            this.items = items;
        }

        public bool Equals(ValueList<T> other) => other != null && CollectionsMarshal.AsSpan(items).SequenceEqual(CollectionsMarshal.AsSpan(other.items), EqualityComparer<T>.Default);
        public override bool Equals(object obj) => Equals(obj as ValueList<T>);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var item in items) hash.Add(item);
            return hash.ToHashCode();
        }
    }

    internal sealed record GeometrySnapshot(
        ValueList<(NodeId Node, double X, double Y, double Height)> Cards,
        ValueList<(NodeId Node, string Name, string Target)> Wiring,
        ValueList<(string Id, double? Y)> Buses,
        ValueList<(string Device, string Port, string Bus)> Ports)
    {
        public static GeometrySnapshot Of(MachineDefinition definition, IReadOnlyList<SchematicNode> placed)
        {
            var cards = new List<(NodeId, double, double, double)>(placed.Count);
            var wiring = new List<(NodeId, string, string)>();
            foreach (var node in placed)
            {
                cards.Add((node.Id, node.Layout.X, node.Layout.Y, node.Height));
                foreach (var (name, target) in node.Wiring) wiring.Add((node.Id, name, target));
            }
            var buses = new List<(string, double?)>(definition.Buses.Count);
            foreach (var bus in definition.Buses) buses.Add((bus.Id, bus.Layout?.Y));
            var ports = new List<(string, string, string)>();
            foreach (var device in definition.Devices)
            {
                foreach (var port in device.Ports()) ports.Add((device.Id, port.Key, port.Value));
            }
            return new GeometrySnapshot(new ValueList<(NodeId, double, double, double)>(cards), new ValueList<(NodeId, string, string)>(wiring),
                new ValueList<(string, double?)>(buses), new ValueList<(string, string, string)>(ports));
        }
    }

    internal sealed class RouteCache
    {
        private readonly Dictionary<WireId, string> paths = new Dictionary<WireId, string>();
        private readonly Dictionary<WireId, IReadOnlyList<(double X, double Y)>> points = new Dictionary<WireId, IReadOnlyList<(double X, double Y)>>();

        private RouteCache(GeometrySnapshot key)
        {
            Key = key;
        }

        public GeometrySnapshot Key { get; }

        public static RouteCache Route(GeometrySnapshot key, IReadOnlyList<SchematicNode> placed, IEnumerable<double> buses, IEnumerable<Segment> taps)
        {
            var requests = WireRequest.From(placed);
            ArrivalSpread.Apply(requests.Select(r => r.Wire));
            new WireRouter(placed.Select(n => n.Bounds).ToList(), buses, taps).Route(requests.Select(r => r.Wire).ToList());
            var cache = new RouteCache(key);
            foreach (var request in requests.Where(r => r.Wire.Points != null))
            {
                cache.paths[request.Id] = WireRouter.Path(request.Wire.Points);
                cache.points[request.Id] = request.Wire.Points.Select(p => (p.X, p.Y)).ToList();
            }
            return cache;
        }

        public string Path(WireId id) => paths.GetValueOrDefault(id);
        public IReadOnlyList<(double X, double Y)> Points(WireId id) => points.GetValueOrDefault(id);
    }
}
