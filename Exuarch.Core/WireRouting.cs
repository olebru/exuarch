using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    // The parts WireRouter puts together to route one wire: the sparse grid it runs on, where it may end, what each
    // step costs, the search problem for A*, and the path that comes out of it.

    // Directions: 0 right, 1 down, 2 left, 3 up.
    internal static class Direction
    {
        public const int Right = 0, Down = 1, Left = 2, Up = 3;
        public static readonly int[] DX = { 1, 0, -1, 0 }, DY = { 0, 1, 0, -1 };
        public static int Opposite(int dir) => (dir + 2) % 4;
    }

    // A point of the grid and the direction the wire was going when it got there.
    internal readonly record struct GridState(int Node, int Dir);

    // The lines a wire may run along: one just outside each edge of every card's keep-out area, and through the
    // start and the goals. A wire stays clear of the keep-out areas and never runs along a bus or a tap, so lines a
    // little way off either side of them are added to cross by.
    internal sealed class RoutingGrid
    {
        // How close to a bus or a tap a wire may run along it.
        public const double Beside = 7;

        private readonly double[] xs;
        private readonly double[] ys;
        private readonly bool[] free;
        private readonly Obstacles blocked;

        private RoutingGrid(double[] xs, double[] ys, Obstacles blocked)
        {
            this.xs = xs;
            this.ys = ys;
            this.blocked = blocked;
            free = new bool[xs.Length * ys.Length];
            for (int node = 0; node < free.Length; node++) free[node] = !blocked.Contains(PointAt(node));
        }

        public static RoutingGrid Build(Obstacles blocked, Point start, IEnumerable<Point> goals)
        {
            var xs = new SortedSet<double>(Lanes(blocked.Rects.SelectMany(r => new[] { r.X - 0.5, r.Right + 0.5 }))) { start.X };
            var ys = new SortedSet<double>(Lanes(blocked.Rects.SelectMany(r => new[] { r.Y - 0.5, r.Bottom + 0.5 }))) { start.Y };
            foreach (var g in goals) { xs.Add(g.X); ys.Add(g.Y); }
            foreach (var y in blocked.Buses) { ys.Add(y - 2 * Beside); ys.Add(y + 2 * Beside); }
            foreach (var t in blocked.Taps) { xs.Add(t.A.X - 2 * Beside); xs.Add(t.A.X + 2 * Beside); }
            return new RoutingGrid(xs.ToArray(), ys.ToArray(), blocked);
        }

        // The lines wires can run along, one just outside each card edge. Two that are closer than two lanes, on either
        // side of a narrow gap between cards, become one down the middle of it.
        private static IEnumerable<double> Lanes(IEnumerable<double> edges)
        {
            var sorted = edges.Distinct().OrderBy(v => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i + 1 < sorted.Count && sorted[i + 1] - sorted[i] < 2 * WireRouter.Lane)
                {
                    yield return (sorted[i] + sorted[i + 1]) / 2;
                    i++;
                }
                else yield return sorted[i];
            }
        }

        public int NodeCount => free.Length;
        public Point PointAt(int node) => new Point(xs[node % xs.Length], ys[node / xs.Length]);
        // The node at a point that is one of the grid's.
        public int NodeAt(Point p) => Array.IndexOf(ys, p.Y) * xs.Length + Array.IndexOf(xs, p.X);

        // The neighbour in a direction, or -1 when the step there is not clear.
        public int Neighbour(int node, int dir)
        {
            int ix = node % xs.Length + Direction.DX[dir], iy = node / xs.Length + Direction.DY[dir];
            if (ix < 0 || iy < 0 || ix >= xs.Length || iy >= ys.Length) return -1;
            int next = iy * xs.Length + ix;
            if (!free[next]) return -1;
            var step = new Segment(PointAt(node), PointAt(next));
            return blocked.Crosses(step) || blocked.RunsAlong(step, Beside) ? -1 : next;
        }
    }

    // Where a wire may end: just outside a side of its target, with the point on the side it goes in at and the
    // direction it has to travel to go in there.
    internal readonly record struct Goal(Point At, int Dir, Point Edge);

    internal static class GoalSet
    {
        // One goal on each side of the target, moved along the side by the wire's spread, leaving out those that are in
        // the way of another card or would run along a bus or a tap.
        public static List<Goal> Around(Rect target, double spread, Obstacles blocked)
        {
            double cx = target.X + target.Width / 2 + Math.Clamp(spread, -target.Width / 2 + 8, target.Width / 2 - 8);
            double cy = target.Y + target.Height / 2 + Math.Clamp(spread, -target.Height / 2 + 8, target.Height / 2 - 8);
            var goals = new List<Goal>
            {
                new Goal(new Point(target.X - WireRouter.Margin, cy), Direction.Right, new Point(target.X, cy)),
                new Goal(new Point(target.Right + WireRouter.Margin, cy), Direction.Left, new Point(target.Right, cy)),
                new Goal(new Point(cx, target.Y - WireRouter.Margin), Direction.Down, new Point(cx, target.Y)),
                new Goal(new Point(cx, target.Bottom + WireRouter.Margin), Direction.Up, new Point(cx, target.Bottom)),
            };
            goals.RemoveAll(g => blocked.Contains(g.At) || blocked.RunsAlong(new Segment(g.At, g.Edge), RoutingGrid.Beside));
            return goals;
        }
    }

    // How many routed wires run along each stretch of the grid, not counting the stretches out of a socket and into a
    // card.
    internal sealed class UsageMap
    {
        private readonly Dictionary<Segment, int> used = new Dictionary<Segment, int>();

        public int Count(Segment s) => used.GetValueOrDefault(s.Normalized);

        public void Add(IReadOnlyList<Point> points)
        {
            for (int i = 1; i + 2 < points.Count; i++)
            {
                var key = new Segment(points[i], points[i + 1]).Normalized;
                used[key] = used.GetValueOrDefault(key) + 1;
            }
        }
    }

    // What a route costs: its length, more for every wire already along a stretch, and extra for every bend. Arriving
    // at a goal going the wrong way costs the bends it takes to turn in, two for a U-turn.
    internal sealed class RouteCostModel
    {
        private const double BendCost = 40;
        private const double SharedCost = 3;
        private readonly UsageMap usage;

        public RouteCostModel(UsageMap usage)
        {
            this.usage = usage;
        }

        public double Step(Segment step, int dir, int previousDir)
        {
            return step.Length * (1 + SharedCost * usage.Count(step)) + (dir == previousDir ? 0 : BendCost);
        }

        public double Arrival(int dir, int goalDir)
        {
            return dir == goalDir ? 0 : BendCost * (dir == Direction.Opposite(goalDir) ? 2 : 1);
        }
    }

    // One wire's route as a search over (grid point, direction): no turning back, and the search may end on a goal.
    internal sealed class WireSearch : ISearchProblem<GridState>
    {
        private readonly RoutingGrid grid;
        private readonly List<Goal> goals;
        private readonly RouteCostModel costs;
        private readonly Dictionary<int, Goal> goalAt = new Dictionary<int, Goal>();

        public WireSearch(RoutingGrid grid, List<Goal> goals, RouteCostModel costs)
        {
            this.grid = grid;
            this.goals = goals;
            this.costs = costs;
            foreach (var goal in goals) goalAt[grid.NodeAt(goal.At)] = goal;
        }

        public Goal GoalAt(int node) => goalAt[node];

        public int StateCount => grid.NodeCount * 4;
        public int IndexOf(GridState state) => state.Node * 4 + state.Dir;

        public double Heuristic(GridState state)
        {
            var at = grid.PointAt(state.Node);
            double best = double.MaxValue;
            foreach (var g in goals) best = Math.Min(best, at.Distance(g.At));
            return best;
        }

        public double? ArrivalCost(GridState state)
        {
            return goalAt.TryGetValue(state.Node, out var goal) ? costs.Arrival(state.Dir, goal.Dir) : null;
        }

        public void Steps(GridState state, List<(GridState Next, double Cost)> steps)
        {
            for (int d = 0; d < 4; d++)
            {
                int next = d == Direction.Opposite(state.Dir) ? -1 : grid.Neighbour(state.Node, d);
                if (next < 0) continue;
                steps.Add((new GridState(next, d), costs.Step(new Segment(grid.PointAt(state.Node), grid.PointAt(next)), d, state.Dir)));
            }
        }
    }

    // The corners of a routed wire: from its socket, along the grid, onto the side of the card it goes in at.
    internal static class PathBuilder
    {
        public static List<Point> Build(Point socket, IEnumerable<Point> through, Point edge)
        {
            var points = new List<Point> { socket };
            points.AddRange(through);
            points.Add(edge);
            return Simplify(points);
        }

        // Drops repeated points and points in the middle of a straight stretch.
        private static List<Point> Simplify(List<Point> points)
        {
            var result = new List<Point>();
            foreach (var p in points)
            {
                if (result.Count > 0 && result[^1] == p) continue;
                if (result.Count >= 2 && InLine(result[^2], result[^1], p)) result[^1] = p;
                else result.Add(p);
            }
            return result;
        }

        private static bool InLine(Point a, Point b, Point c) => (a.X == b.X && b.X == c.X) || (a.Y == b.Y && b.Y == c.Y);
    }

    // Wires that run along the same line over the same stretch are moved apart, one lane each. The first and the last
    // stretch, which leave the socket and enter the card, stay put, and a stretch only moves when it and the two it
    // joins stay clear of the cards.
    internal sealed class LaneSeparator
    {
        private readonly record struct Stretch(WireRouter.Wire Wire, int Index, Segment Segment);

        private readonly Obstacles cards;

        public LaneSeparator(Obstacles cards)
        {
            this.cards = cards;
        }

        public void Separate(IEnumerable<WireRouter.Wire> wires)
        {
            var stretches = wires.SelectMany(Stretches).ToList();
            foreach (var line in stretches.GroupBy(s => (s.Segment.Horizontal, s.Segment.At)))
            {
                foreach (var cluster in Clusters(line.OrderBy(s => s.Segment.From))) SpreadOut(cluster);
            }
        }

        private static IEnumerable<Stretch> Stretches(WireRouter.Wire wire)
        {
            for (int i = 1; i + 2 < wire.Points.Count; i++) yield return new Stretch(wire, i, new Segment(wire.Points[i], wire.Points[i + 1]));
        }

        // Runs of stretches, in order along the line, that overlap one another.
        private static List<List<Stretch>> Clusters(IEnumerable<Stretch> ordered)
        {
            var clusters = new List<List<Stretch>>();
            double reach = double.MinValue;
            foreach (var s in ordered)
            {
                if (clusters.Count == 0 || s.Segment.From >= reach) clusters.Add(new List<Stretch>());
                clusters[^1].Add(s);
                reach = Math.Max(reach, s.Segment.To);
            }
            return clusters;
        }

        // Each wire in the cluster gets a lane of its own, spread evenly about the line.
        private void SpreadOut(List<Stretch> cluster)
        {
            var owners = cluster.Select(s => s.Wire).Distinct().ToList();
            if (owners.Count < 2) return;
            for (int k = 0; k < owners.Count; k++)
            {
                double offset = (k - (owners.Count - 1) / 2.0) * WireRouter.Lane;
                foreach (var s in cluster.Where(s => s.Wire == owners[k])) Shift(s, offset);
            }
        }

        // Moves a stretch across its line, as its wire's points are now: an earlier move may have shifted one end.
        private void Shift(Stretch stretch, double offset)
        {
            var points = stretch.Wire.Points;
            int index = stretch.Index;
            bool horizontal = stretch.Segment.Horizontal;
            var moved = new Segment(Across(points[index], horizontal, offset), Across(points[index + 1], horizontal, offset));
            if (cards.Crosses(moved) || cards.Crosses(new Segment(points[index - 1], moved.A)) || cards.Crosses(new Segment(moved.B, points[index + 2]))
                || cards.RunsAlong(moved, RoutingGrid.Beside)) return;
            points[index] = moved.A;
            points[index + 1] = moved.B;
        }

        private static Point Across(Point p, bool horizontal, double by) => horizontal ? new Point(p.X, p.Y + by) : new Point(p.X + by, p.Y);
    }
}
