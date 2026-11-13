using System;
namespace Exuarch.Core
{
    internal sealed class TriangleSetup
    {
        private readonly struct Corner
        {
            public readonly long X, Y;
            public readonly int Z, Colour;
            public Corner(int[] words, int index)
            {
                X = 2L * (short)words[index * 4];
                Y = 2L * (short)words[index * 4 + 1];
                Z = words[index * 4 + 2];
                Colour = words[index * 4 + 3];
            }
        }

        private readonly Corner v0, v1, v2;
        private readonly long area;
        private readonly bool swapped;
        private readonly long onEdgeBias0, onEdgeBias1, onEdgeBias2;
        public int Left { get; }
        public int Right { get; }
        public int Top { get; }
        public int Bottom { get; }

        private TriangleSetup(Corner v0, Corner v1, Corner v2, long area, bool swapped)
        {
            (this.v0, this.v1, this.v2, this.area, this.swapped) = (v0, v1, v2, area, swapped);
            onEdgeBias0 = TopLeftRuleBias(v1, v2);
            onEdgeBias1 = TopLeftRuleBias(v2, v0);
            onEdgeBias2 = TopLeftRuleBias(v0, v1);
            Left = (int)Math.Max(0, Math.Min(v0.X, Math.Min(v1.X, v2.X)) / 2 - 1);
            Right = (int)Math.Min(Framebuffer.Width - 1, Math.Max(v0.X, Math.Max(v1.X, v2.X)) / 2 + 1);
            Top = (int)Math.Max(0, Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)) / 2 - 1);
            Bottom = (int)Math.Min(Framebuffer.Height - 1, Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)) / 2 + 1);
        }

        public static TriangleSetup From(int[] words)
        {
            Corner v0 = new Corner(words, 0), v1 = new Corner(words, 1), v2 = new Corner(words, 2);
            long area = Edge(v0, v1, v2.X, v2.Y);
            if (area == 0) return null;
            return area > 0 ? new TriangleSetup(v0, v1, v2, area, false) : new TriangleSetup(v0, v2, v1, -area, true);
        }

        public bool Span(int y, out int first, out int last)
        {
            first = last = -1;
            for (int x = Left; x <= Right; x++)
            {
                if (Inside(2L * x + 1, 2L * y + 1))
                {
                    if (first < 0) first = x;
                    last = x;
                }
                else if (first >= 0) break;
            }
            return first >= 0;
        }

        public (int Colour, int Z) Fragment(int x, int y)
        {
            long px = 2L * x + 1, py = 2L * y + 1;
            long w0 = Edge(v1, v2, px, py), w1 = Edge(v2, v0, px, py), w2 = area - w0 - w1;
            int z = (int)Math.Clamp((v0.Z * w0 + v1.Z * w1 + v2.Z * w2 + area / 2) / area, 0, 0xFFFF);
            return (Blend(w0, w1, w2), z);
        }

        public (long W0, long W1, long W2, long Area) Weights(int x, int y)
        {
            long px = 2L * x + 1, py = 2L * y + 1;
            long w0 = Edge(v1, v2, px, py), w1 = Edge(v2, v0, px, py), w2 = area - w0 - w1;
            return swapped ? (w0, w2, w1, area) : (w0, w1, w2, area);
        }

        private bool Inside(long px, long py)
        {
            return Edge(v1, v2, px, py) + onEdgeBias0 >= 0 && Edge(v2, v0, px, py) + onEdgeBias1 >= 0 && Edge(v0, v1, px, py) + onEdgeBias2 >= 0;
        }

        private static long Edge(Corner a, Corner b, long px, long py)
        {
            return (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
        }
        private static long TopLeftRuleBias(Corner a, Corner b)
        {
            return IsTopOrLeftEdge(a, b) ? 0 : -1;
        }
        private static bool IsTopOrLeftEdge(Corner a, Corner b)
        {
            long dx = b.X - a.X, dy = b.Y - a.Y;
            return (dy == 0 && dx > 0) || dy < 0;
        }

        private int Blend(long w0, long w1, long w2)
        {
            return (Channel(11, 0x1F, w0, w1, w2) << 11) | (Channel(5, 0x3F, w0, w1, w2) << 5) | Channel(0, 0x1F, w0, w1, w2);
        }
        private int Channel(int shift, int mask, long w0, long w1, long w2)
        {
            long sum = ((v0.Colour >> shift) & mask) * w0 + ((v1.Colour >> shift) & mask) * w1 + ((v2.Colour >> shift) & mask) * w2;
            return (int)Math.Clamp((sum + area / 2) / area, 0, mask);
        }
    }
}
