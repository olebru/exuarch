using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Exuarch.Core
{
    // Routes wires between cards as right angled paths that go around the cards instead of over them, the way a
    // schematic editor draws them. Each wire leaves its socket to the right and enters its target card from whichever
    // side is cheapest. The search runs on a sparse grid made of the cards' edges, so it stays small however large the
    // canvas is: A* over (point, direction), where a bend costs extra and running along an earlier wire costs more.
    // Wires that still share a stretch are then moved apart into lanes of their own. The parts are in WireRouting.
    internal class WireRouter
    {
        public class Wire
        {
            public Point Start;
            public int Target;
            // Spreads the wires that end on the same card along its side, so their arrows do not land on top of each other.
            public double Spread;
            public List<Point> Points;
        }

        // Space kept free around every card, and between wires that run side by side.
        public const double Margin = 12;
        public const double Lane = 6;

        private readonly IReadOnlyList<Rect> cards;
        // The cards, and the lines a wire may cross but never run along: the buses, and the taps from ports down or up
        // to them. Wires keep a margin from the cards, so the search sees them grown by it.
        private readonly Obstacles cardObstacles;
        private readonly Obstacles blocked;

        public WireRouter(IReadOnlyList<Rect> cards, IEnumerable<double> buses = null, IEnumerable<Segment> taps = null)
        {
            this.cards = cards;
            cardObstacles = new Obstacles(cards, buses, taps);
            blocked = cardObstacles.Inflate(Margin - 0.5);
        }

        // Routes every wire, shortest first, then separates the stretches they share. A wire that can not be routed
        // keeps Points null.
        public void Route(IList<Wire> wires)
        {
            var usage = new UsageMap();
            foreach (var wire in wires.OrderBy(w => w.Start.Distance(cards[w.Target].Centre)))
            {
                wire.Points = RouteOne(wire, usage);
                if (wire.Points != null) usage.Add(wire.Points);
            }
            new LaneSeparator(cardObstacles).Separate(wires.Where(w => w.Points != null));
        }

        private List<Point> RouteOne(Wire wire, UsageMap usage)
        {
            var start = new Point(wire.Start.X + Margin, wire.Start.Y);
            if (blocked.Contains(start)) return null;
            var goals = GoalSet.Around(cards[wire.Target], wire.Spread, blocked);
            if (goals.Count == 0) return null;
            var grid = RoutingGrid.Build(blocked, start, goals.Select(g => g.At));
            var search = new WireSearch(grid, goals, new RouteCostModel(usage));
            var path = AStar.Search(search, new GridState(grid.NodeAt(start), Direction.Right));
            if (path == null) return null;
            return PathBuilder.Build(wire.Start, path.Select(s => grid.PointAt(s.Node)), search.GoalAt(path[^1].Node).Edge);
        }

        // An SVG path through the points, with the corners rounded a little.
        public static string Path(IReadOnlyList<Point> points, double radius = 6)
        {
            string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            var path = new System.Text.StringBuilder($"M {N(points[0].X)} {N(points[0].Y)}");
            for (int i = 1; i < points.Count; i++)
            {
                var p = points[i];
                if (i == points.Count - 1) { path.Append($" L {N(p.X)} {N(p.Y)}"); break; }
                var (prev, next) = (points[i - 1], points[i + 1]);
                double r = Math.Min(radius, Math.Min(prev.Distance(p), p.Distance(next)) / 2);
                var a = p.Toward(prev, r);
                var b = p.Toward(next, r);
                path.Append($" L {N(a.X)} {N(a.Y)} Q {N(p.X)} {N(p.Y)} {N(b.X)} {N(b.Y)}");
            }
            return path.ToString();
        }
    }
}
