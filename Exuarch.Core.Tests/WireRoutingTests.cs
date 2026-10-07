using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class WireRoutingTests
{
    // A row of cells 0..9 where a step right costs 1 and a jump of two costs 3, ending at cell 9 or, dearer, at cell 5.
    private sealed class Line : ISearchProblem<int>
    {
        public int StateCount => 10;
        public int IndexOf(int state) => state;
        public double Heuristic(int state) => 0;
        public double? ArrivalCost(int state) => state switch { 9 => 0, 5 => 10, _ => null };

        public void Steps(int state, List<(int Next, double Cost)> steps)
        {
            if (state + 1 < 10) steps.Add((state + 1, 1));
            if (state + 2 < 10) steps.Add((state + 2, 3));
        }
    }

    [Fact]
    public void AStarFindsTheCheapestGoalCountingWhatItCostsToArrive()
    {
        var path = AStar.Search(new Line(), 0);
        Assert.Equal(Enumerable.Range(0, 10), path);
    }

    [Fact]
    public void AStarReturnsNullWhenNoGoalCanBeReached()
    {
        Assert.Null(AStar.Search(new Stuck(), 0));
    }

    // Two states and no step between them.
    private sealed class Stuck : ISearchProblem<int>
    {
        public int StateCount => 2;
        public int IndexOf(int state) => state;
        public double Heuristic(int state) => 0;
        public double? ArrivalCost(int state) => state == 1 ? 0 : null;
        public void Steps(int state, List<(int Next, double Cost)> steps) { }
    }

    [Fact]
    public void AWireGoesAroundTheCardInItsWay()
    {
        // The wire leaves the left card's socket; a card in between stands in the way of the straight line to the right one.
        var cards = new List<Rect> { new Rect(0, 0, 160, 100), new Rect(250, 0, 160, 100), new Rect(500, 0, 160, 100) };
        var wire = new WireRouter.Wire { Start = new Point(160, 50), Target = 2 };
        new WireRouter(cards).Route(new[] { wire });
        Assert.NotNull(wire.Points);
        Assert.Equal(wire.Start, wire.Points[0]);
        for (int i = 1; i < wire.Points.Count; i++)
        {
            var stretch = new Segment(wire.Points[i - 1], wire.Points[i]);
            Assert.True(stretch.A.X == stretch.B.X || stretch.A.Y == stretch.B.Y, "every stretch is horizontal or vertical");
            Assert.False(cards[1].Crosses(stretch), "no stretch crosses the card in between");
        }
        var end = wire.Points[^1];
        var target = cards[2];
        Assert.True(end.X == target.X || end.X == target.Right || end.Y == target.Y || end.Y == target.Bottom, "it ends on the target's side");
    }

    [Fact]
    public void WiresAlongTheSameStretchGetLanesOfTheirOwn()
    {
        var cards = new List<Rect> { new Rect(0, 0, 160, 100), new Rect(0, 300, 160, 100), new Rect(600, 150, 160, 100) };
        var wires = new[] { new WireRouter.Wire { Start = new Point(160, 40), Target = 2 }, new WireRouter.Wire { Start = new Point(160, 340), Target = 2 } };
        new WireRouter(cards).Route(wires);
        var stretches = wires.SelectMany(w => w.Points.Zip(w.Points.Skip(1), (a, b) => new Segment(a, b))).ToList();
        // No two stretches of different wires lie on top of each other.
        var lines = stretches.Where(s => s.Length > 0).GroupBy(s => (s.Horizontal, s.At)).Where(g => g.Count() > 1);
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(s => s.From).ToList();
            for (int i = 1; i < ordered.Count; i++) Assert.True(ordered[i].From >= ordered[i - 1].To, $"overlap on the line at {line.Key.At}");
        }
    }

    [Fact]
    public void ThePathRoundsItsCorners()
    {
        var path = WireRouter.Path(new[] { new Point(0, 0), new Point(20, 0), new Point(20, 20) });
        Assert.Equal("M 0 0 L 14 0 Q 20 0 20 6 L 20 20", path);
    }

    [Fact]
    public void TheDecoderIsANodeLikeTheDevices()
    {
        var machine = BuiltInPackages.Default.Machine.Clone();
        machine.EnsureLayout(force: true);
        var nodes = SchematicNode.Of(machine, DeviceRegistry.CreateDefault()).ToList();
        Assert.Equal(machine.Devices.Count + 1, nodes.Count);
        var decoder = nodes[^1];
        Assert.Equal(NodeId.Decoder, decoder.Id);
        Assert.Equal(NodeId.Decoder, NodeId.Of("decoder", null));
        Assert.Equal(new[] { machine.Decoder.Status, machine.Decoder.InstructionRegister, machine.Decoder.Interrupts }, decoder.SocketTargets);
        Assert.Empty(decoder.BusIds);
        Assert.Equal(SchematicLayout.DecoderHeight, decoder.Bounds.Height);
        Assert.All(nodes, n => Assert.True(n.Placed));
        Assert.True(nodes[0].Is(machine.Devices[0]));
        Assert.False(decoder.Is(machine.Devices[0]));
    }
}
