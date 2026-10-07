using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    // A point on the schematic canvas.
    internal readonly record struct Point(double X, double Y)
    {
        // How far apart two points are going along the grid lines.
        public double Distance(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        // The point a distance along the straight line towards another, or this one when they are the same.
        public Point Toward(Point to, double by)
        {
            double length = Distance(to);
            return length == 0 ? this : new Point(X + (to.X - X) / length * by, Y + (to.Y - Y) / length * by);
        }
    }

    // A straight stretch between two points, horizontal or vertical as wires run.
    internal readonly record struct Segment(Point A, Point B)
    {
        // A stretch of no length counts as horizontal.
        public bool Horizontal => A.Y == B.Y;
        // The line it lies on: its y when horizontal, its x when vertical.
        public double At => Horizontal ? A.Y : A.X;
        // Where along that line it starts and ends.
        public double From => Horizontal ? Math.Min(A.X, B.X) : Math.Min(A.Y, B.Y);
        public double To => Horizontal ? Math.Max(A.X, B.X) : Math.Max(A.Y, B.Y);
        public double Left => Math.Min(A.X, B.X);
        public double Right => Math.Max(A.X, B.X);
        public double Top => Math.Min(A.Y, B.Y);
        public double Bottom => Math.Max(A.Y, B.Y);
        public double Length => A.Distance(B);

        // The same stretch with its ends in a fixed order, so it is the same whichever way a wire ran along it.
        public Segment Normalized => A.X < B.X || (A.X == B.X && A.Y < B.Y) ? this : new Segment(B, A);
    }

    // A rectangle on the canvas, such as the area a card covers.
    internal readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public Point Centre => new Point(X + Width / 2, Y + Height / 2);

        public Rect Inflate(double by) => new Rect(X - by, Y - by, Width + 2 * by, Height + 2 * by);

        // Inside, not on the edge.
        public bool Contains(Point p) => p.X > X && p.X < Right && p.Y > Y && p.Y < Bottom;

        // Whether a stretch passes through the inside; one that only runs along the edge does not.
        public bool Crosses(Segment s) => s.Right > X && s.Left < Right && s.Bottom > Y && s.Top < Bottom;

        // Whether the two come closer than the gap, or overlap.
        public bool Intersects(Rect other, double gap = 0) => X < other.Right + gap && other.X < Right + gap && Y < other.Bottom + gap && other.Y < Bottom + gap;
    }

    // What cards and wires keep clear of: the cards' rectangles, plus the lines a wire may cross but never run along,
    // the buses and the taps from ports down or up to them.
    internal sealed class Obstacles
    {
        private readonly Rect[] rects;
        private readonly double[] buses;
        private readonly Segment[] taps;

        public Obstacles(IEnumerable<Rect> rects, IEnumerable<double> buses = null, IEnumerable<Segment> taps = null)
        {
            this.rects = rects.ToArray();
            this.buses = buses?.ToArray() ?? Array.Empty<double>();
            this.taps = taps?.ToArray() ?? Array.Empty<Segment>();
        }

        public IReadOnlyList<Rect> Rects => rects;
        public IReadOnlyList<double> Buses => buses;
        public IReadOnlyList<Segment> Taps => taps;

        // The same obstacles with every rectangle grown on all sides.
        public Obstacles Inflate(double by) => new Obstacles(rects.Select(r => r.Inflate(by)), buses, taps);

        public bool Contains(Point p)
        {
            foreach (var r in rects) if (r.Contains(p)) return true;
            return false;
        }

        public bool Crosses(Segment s)
        {
            foreach (var r in rects) if (r.Crosses(s)) return true;
            return false;
        }

        // Whether a stretch would lie along a bus or a tap, nearer to it than the distance given.
        public bool RunsAlong(Segment s, double within)
        {
            if (s.Horizontal)
            {
                foreach (var y in buses) if (Math.Abs(s.A.Y - y) < within) return true;
                return false;
            }
            foreach (var t in taps) if (Math.Abs(s.A.X - t.A.X) < within && s.Bottom > t.Top && s.Top < t.Bottom) return true;
            return false;
        }

        // Whether an area keeps the gap to every rectangle and bus.
        public bool IsClear(Rect area, double gap)
        {
            if (buses.Any(y => y > area.Y - gap && y < area.Bottom + gap)) return false;
            return !rects.Any(r => area.Intersects(r, gap));
        }
    }
}
