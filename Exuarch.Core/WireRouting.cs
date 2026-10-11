using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    internal static class Direction
    {
        public const int Right = 0, Down = 1, Left = 2, Up = 3;
        public static readonly int[] DX = { 1, 0, -1, 0 }, DY = { 0, 1, 0, -1 };
        public static int Opposite(int dir) => (dir + 2) % 4;
    }

    internal readonly record struct GridState(int Node, int Dir);

    internal sealed class RoutingGrid
    {
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
        public int NodeAt(Point p) => Array.IndexOf(ys, p.Y) * xs.Length + Array.IndexOf(xs, p.X);

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

    internal readonly record struct Goal(Point At, int Dir, Point Edge);

    internal static class GoalSet
    {
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

    internal static class PathBuilder
    {
        public static List<Point> Build(Point socket, IEnumerable<Point> through, Point edge)
        {
            var points = new List<Point> { socket };
            points.AddRange(through);
            points.Add(edge);
            return Simplify(points);
        }

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
