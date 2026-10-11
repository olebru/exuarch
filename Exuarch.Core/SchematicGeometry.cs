using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core
{
    internal readonly record struct Point(double X, double Y)
    {
        public double Distance(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public Point Toward(Point to, double by)
        {
            double length = Distance(to);
            return length == 0 ? this : new Point(X + (to.X - X) / length * by, Y + (to.Y - Y) / length * by);
        }
    }

    internal readonly record struct Segment(Point A, Point B)
    {
        public bool Horizontal => A.Y == B.Y;
        public double At => Horizontal ? A.Y : A.X;
        public double From => Horizontal ? Math.Min(A.X, B.X) : Math.Min(A.Y, B.Y);
        public double To => Horizontal ? Math.Max(A.X, B.X) : Math.Max(A.Y, B.Y);
        public double Left => Math.Min(A.X, B.X);
        public double Right => Math.Max(A.X, B.X);
        public double Top => Math.Min(A.Y, B.Y);
        public double Bottom => Math.Max(A.Y, B.Y);
        public double Length => A.Distance(B);

        public Segment Normalized => A.X < B.X || (A.X == B.X && A.Y < B.Y) ? this : new Segment(B, A);
    }

    internal readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public Point Centre => new Point(X + Width / 2, Y + Height / 2);

        public Rect Inflate(double by) => new Rect(X - by, Y - by, Width + 2 * by, Height + 2 * by);

        public bool Contains(Point p) => p.X > X && p.X < Right && p.Y > Y && p.Y < Bottom;

        public bool Crosses(Segment s) => s.Right > X && s.Left < Right && s.Bottom > Y && s.Top < Bottom;

        public bool Intersects(Rect other, double gap = 0) => X < other.Right + gap && other.X < Right + gap && Y < other.Bottom + gap && other.Y < Bottom + gap;
    }

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

        public bool IsClear(Rect area, double gap)
        {
            if (buses.Any(y => y > area.Y - gap && y < area.Bottom + gap)) return false;
            return !rects.Any(r => area.Intersects(r, gap));
        }
    }
}
