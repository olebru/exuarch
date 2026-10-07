using System;
namespace Exuarch.Core
{
    // A triangle as the rasterizer fills it: the corners in the order that makes its area positive, the box of
    // pixels on the screen it can cover, and for each pixel whether its centre is inside and what colour and depth it
    // gets. Coordinates are doubled so pixel centres are whole numbers.
    internal sealed class TriangleSetup
    {
        private readonly struct Corner
        {
            public readonly long X, Y;
            public readonly int Z, Colour;
            public Corner(int[] words, int index)
            {
                // Coordinates are signed, so a corner can be off the screen.
                X = 2L * (short)words[index * 4];
                Y = 2L * (short)words[index * 4 + 1];
                Z = words[index * 4 + 2];
                Colour = words[index * 4 + 3];
            }
        }

        private readonly Corner v0, v1, v2;
        private readonly long area;
        // Pixels exactly on an edge belong to the triangle only if the edge is a top or a left edge: 0 for those, -1
        // for the others. One per edge, named by the corner opposite it.
        private readonly long bias0, bias1, bias2;
        public int Left { get; }
        public int Right { get; }
        public int Top { get; }
        public int Bottom { get; }

        private TriangleSetup(Corner v0, Corner v1, Corner v2, long area)
        {
            (this.v0, this.v1, this.v2, this.area) = (v0, v1, v2, area);
            bias0 = TopLeft(v1, v2) ? 0 : -1;
            bias1 = TopLeft(v2, v0) ? 0 : -1;
            bias2 = TopLeft(v0, v1) ? 0 : -1;
            Left = (int)Math.Max(0, Math.Min(v0.X, Math.Min(v1.X, v2.X)) / 2 - 1);
            Right = (int)Math.Min(Framebuffer.Width - 1, Math.Max(v0.X, Math.Max(v1.X, v2.X)) / 2 + 1);
            Top = (int)Math.Max(0, Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)) / 2 - 1);
            Bottom = (int)Math.Min(Framebuffer.Height - 1, Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)) / 2 + 1);
        }

        // The triangle in the 12 words the rasterizer reads (x, y, z and colour per corner), or null when it has no
        // area and so covers no pixel.
        public static TriangleSetup From(int[] words)
        {
            Corner v0 = new Corner(words, 0), v1 = new Corner(words, 1), v2 = new Corner(words, 2);
            long area = Edge(v0, v1, v2.X, v2.Y);
            if (area == 0) return null;
            return area > 0 ? new TriangleSetup(v0, v1, v2, area) : new TriangleSetup(v0, v2, v1, -area);
        }

        // The span of a row: the first and last pixels whose centre is inside. False when there are none.
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

        // The colour of a pixel, blended from the corners by how near it is to each (Gouraud shading), and its depth.
        public (int Colour, int Z) Fragment(int x, int y)
        {
            long px = 2L * x + 1, py = 2L * y + 1;
            long w0 = Edge(v1, v2, px, py), w1 = Edge(v2, v0, px, py), w2 = area - w0 - w1;
            int z = (int)Math.Clamp((v0.Z * w0 + v1.Z * w1 + v2.Z * w2 + area / 2) / area, 0, 0xFFFF);
            return (Blend(w0, w1, w2), z);
        }

        private bool Inside(long px, long py)
        {
            return Edge(v1, v2, px, py) + bias0 >= 0 && Edge(v2, v0, px, py) + bias1 >= 0 && Edge(v0, v1, px, py) + bias2 >= 0;
        }

        // Twice the signed area of the triangle a, b, p: positive when p is on the inside of the edge from a to b.
        private static long Edge(Corner a, Corner b, long px, long py)
        {
            return (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
        }
        // With the corners in the order that makes the area positive (y growing downwards), the inside is on the
        // right of each edge as it runs from a to b. So a left edge runs upwards, and a top edge runs to the right.
        private static bool TopLeft(Corner a, Corner b)
        {
            long dx = b.X - a.X, dy = b.Y - a.Y;
            return (dy == 0 && dx > 0) || dy < 0;
        }

        // RGB565 corner colours mixed channel by channel by the pixel's weights.
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
