using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class SchematicLayoutTests
{
    private static readonly DeviceRegistry Registry = DeviceRegistry.CreateDefault();

    [Fact]
    public void BuiltInMachinesHaveNoCardsOnTopOfEachOtherOrOnABus()
    {
        foreach (var package in BuiltInPackages.All)
        {
            // A copy: the built in packages are shared by every test.
            var machine = package.Machine.Clone();
            machine.EnsureLayout();
            var cards = new SchematicLayout(machine, Registry).Cards().ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                for (int j = i + 1; j < cards.Count; j++)
                {
                    var (a, b) = (cards[i], cards[j]);
                    bool overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                    Assert.False(overlap, $"{package.Name}: {a.Id} and {b.Id} overlap");
                }
                foreach (var bus in machine.Buses)
                {
                    var c = cards[i];
                    Assert.False(bus.Layout.Y > c.Y - 4 && bus.Layout.Y < c.Y + c.Height + 4, $"{package.Name}: {c.Id} lies across bus {bus.Id}");
                }
            }
        }
    }

    [Fact]
    public void SocketsFitInsideTheirCard()
    {
        foreach (var info in Registry.TypeInfos.Where(t => t.Connections.Count > 0))
        {
            var height = SchematicLayout.HeightFor(info.Connections.Count);
            Assert.True(SchematicLayout.SocketRowY(info.Connections.Count - 1) + SchematicLayout.SocketRowHeight / 2 <= height, info.Type);
        }
        Assert.Equal(SchematicLayout.CardMinHeight, SchematicLayout.HeightFor(0));
        Assert.True(SchematicLayout.DecoderHeight > SchematicLayout.CardMinHeight);
    }

    [Fact]
    public void PortsFaceTheirBus()
    {
        var machine = BuiltInPackages.Get("HARVARD-16").Machine;
        var layout = new SchematicLayout(machine, Registry);
        foreach (var device in machine.Devices)
        {
            foreach (var port in device.Ports())
            {
                var anchor = layout.PortAnchor(device, port.Key);
                var busY = layout.BusY(port.Value);
                Assert.Equal(busY > device.Layout.Y, anchor.Bottom);
                Assert.InRange(anchor.X, device.Layout.X, device.Layout.X + SchematicLayout.CardWidth);
            }
        }
    }

    // Both the shipped layouts and what Auto layout makes of every built in machine.
    private static System.Collections.Generic.IEnumerable<(string Name, MachineDefinition Machine)> Layouts()
    {
        foreach (var package in BuiltInPackages.All)
        {
            var shipped = package.Machine.Clone();
            shipped.EnsureLayout();
            yield return (package.Name, shipped);
            var auto = shipped.Clone();
            auto.EnsureLayout(force: true);
            yield return (package.Name + " auto", auto);
        }
    }

    private static bool Through((double X, double Y) a, (double X, double Y) b, (string Id, double X, double Y, double Width, double Height) card)
    {
        double left = System.Math.Min(a.X, b.X), right = System.Math.Max(a.X, b.X), top = System.Math.Min(a.Y, b.Y), bottom = System.Math.Max(a.Y, b.Y);
        return right > card.X && left < card.X + card.Width && bottom > card.Y && top < card.Y + card.Height;
    }

    [Fact]
    public void PortWiresReachTheirBusWithoutCrossingACard()
    {
        foreach (var (name, machine) in Layouts())
        {
            var layout = new SchematicLayout(machine, Registry);
            var cards = layout.Cards().ToList();
            foreach (var device in machine.Devices)
            {
                foreach (var port in device.Ports())
                {
                    var anchor = layout.PortAnchor(device, port.Key);
                    var bus = (anchor.X, layout.BusY(port.Value));
                    foreach (var card in cards.Where(c => c.Id != device.Id))
                    {
                        Assert.False(Through((anchor.X, anchor.Y), bus, card), $"{name}: {device.Id}.{port.Key} runs through {card.Id}");
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryConnectionIsRoutedAroundTheCards()
    {
        foreach (var (name, machine) in Layouts())
        {
            var layout = new SchematicLayout(machine, Registry);
            var cards = layout.Cards().ToList();
            var wires = new System.Collections.Generic.List<(string Label, System.Collections.Generic.IReadOnlyList<(double X, double Y)> Points)>();
            foreach (var device in machine.Devices)
            {
                var connections = layout.Info(device).Connections;
                for (int i = 0; i < connections.Count; i++)
                {
                    if (device.Connections.ContainsKey(connections[i].Name)) wires.Add(($"{device.Id}.{connections[i].Name}", layout.RoutePoints("device", device.Id, i)));
                }
            }
            for (int i = 0; i < SchematicLayout.DecoderSockets.Length; i++)
            {
                if (layout.DecoderTarget(SchematicLayout.DecoderSockets[i].Name) != null) wires.Add(($"decoder.{SchematicLayout.DecoderSockets[i].Name}", layout.RoutePoints("decoder", null, i)));
            }
            Assert.NotEmpty(wires);
            foreach (var (label, points) in wires)
            {
                Assert.True(points != null, $"{name}: {label} was not routed");
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Assert.True(points[i].X == points[i + 1].X || points[i].Y == points[i + 1].Y, $"{name}: {label} has a slanted stretch");
                    foreach (var card in cards) Assert.False(Through(points[i], points[i + 1], card), $"{name}: {label} runs through {card.Id}");
                }
                // Into the target, not along a bus.
                foreach (var bus in machine.Buses)
                {
                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        bool along = points[i].Y == points[i + 1].Y && System.Math.Abs(points[i].Y - bus.Layout.Y) < 4;
                        Assert.False(along, $"{name}: {label} runs along bus {bus.Id}");
                    }
                }
            }
            // The path drawn is the routed one, and dragging falls back to a curve.
            var alu = machine.Devices.FirstOrDefault(d => d.Type == "alu");
            if (alu != null)
            {
                var target = machine.FindDevice(alu.Connections["a"]);
                Assert.StartsWith("M ", layout.ConnectionPath(alu, 0, target));
                Assert.DoesNotContain(" C ", layout.ConnectionPath(alu, 0, target));
                layout.Draft = true;
                Assert.Contains(" C ", layout.ConnectionPath(alu, 0, target));
            }
        }
    }

    [Fact]
    public void AutoLayoutPutsADeviceOnTwoBusesBetweenThem()
    {
        var machine = BuiltInPackages.Get("FLIP-16").Machine;
        machine.EnsureLayout(force: true);
        double Bus(string id) => machine.FindBus(id).Layout.Y;
        var bridge = machine.FindDevice("bridge").Layout.Y;
        Assert.InRange(bridge, Bus("main"), Bus("list"));
        var rast = machine.FindDevice("rast").Layout.Y;
        Assert.InRange(rast, Bus("list"), Bus("video"));
        // The ALU sits in a row next to the registers it reads.
        var alu = machine.FindDevice("alu").Layout;
        Assert.Contains(machine.Devices, d => d.Id is "a" or "t" && d.Layout.Y == alu.Y && System.Math.Abs(d.Layout.X - alu.X) == SchematicPlacement.ColumnSpacing);
    }
}
