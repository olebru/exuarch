using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Exuarch.Core
{
    internal class WireRouter
    {
        public class Wire
        {
            public Point Start;
            public int StartDirection = Direction.Right;
            public int Target;
            public double Spread;
            public List<Point> Points;
        }

        public const double Margin = 12;
        public const double Lane = 6;

        private readonly IReadOnlyList<Rect> cards;
        private readonly Obstacles cardObstacles;
        private readonly Obstacles blocked;

        public WireRouter(IReadOnlyList<Rect> cards, IEnumerable<double> buses = null, IEnumerable<Segment> taps = null)
        {
            this.cards = cards;
            cardObstacles = new Obstacles(cards, buses, taps);
            blocked = cardObstacles.Inflate(Margin - 0.5);
        }

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
            var start = new Point(wire.Start.X + Direction.DX[wire.StartDirection] * Margin, wire.Start.Y + Direction.DY[wire.StartDirection] * Margin);
            if (blocked.Contains(start)) return null;
            var goals = GoalSet.Around(cards[wire.Target], wire.Spread, blocked);
            if (goals.Count == 0) return null;
            var grid = RoutingGrid.Build(blocked, start, goals.Select(g => g.At));
            var search = new WireSearch(grid, goals, new RouteCostModel(usage));
            var path = AStar.Search(search, new GridState(grid.NodeAt(start), wire.StartDirection));
            if (path == null) return null;
            return PathBuilder.Build(wire.Start, path.Select(s => grid.PointAt(s.Node)), search.GoalAt(path[^1].Node).Edge);
        }

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
