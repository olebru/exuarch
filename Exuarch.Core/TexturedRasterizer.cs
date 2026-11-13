using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public class TexturedRasterizer : ControlLineDevice, IBusDevice, IBusMaster, IInterruptSource
    {
        public const int WordsPerCorner = 6;
        public const int WordsPerTriangle = 3 * WordsPerCorner + 1;
        public const int DefaultSize = 32;
        public const int MaxSize = 256;
        private const int PerspectiveScale = 1 << 16;

        public int ListAddress { get; private set; }
        public int Count { get; private set; }
        public int Size { get; }
        public bool Busy { get; private set; }
        public int Triangle { get; private set; }
        public string Stage { get; private set; } = "idle";
        public long TrianglesDrawn { get; private set; }
        public long PixelsDrawn { get; private set; }
        public long PixelsHidden { get; private set; }
        public long TexelsRead { get; private set; }
        public long JobsDone { get; private set; }

        private enum Kind { Write, Read, Lines }
        private enum Target { None, Word, Depth, Texel }

        private sealed class Step
        {
            public Kind Kind;
            public Bus Bus;
            public Target Target;
            public int Value;
            public Action[] Lines;
            public string[] Readers;

            public Step(Kind kind, Bus bus, Target target, params (IBusDevice Device, string Line)[] lines)
            {
                Kind = kind;
                Bus = bus;
                Target = target;
                Lines = lines.Select(l => ControlLineTable.Bind(l.Device, l.Line)).ToArray();
                Readers = lines.Select(l => l.Device.ID()).Distinct().ToArray();
            }

            public Step With(int value)
            {
                Value = value;
                return this;
            }
        }

        private readonly Step address, word, column, row, plot, texelAddress, texelRead, depthRead, depthWrite, pass;
        private readonly Bus host, list, texture, video;
        private readonly Framebuffer screen;
        private readonly DepthBuffer depth;
        private readonly MemoryModule memory, textureMemory;
        private readonly string deviceID, deviceName;
        private bool start, status;
        private readonly LatchedLines loads = new LatchedLines();
        private IEnumerator<Step[]> job;
        private Step[] pending, executing;
        private int wordValue, depthValue, texelValue;
        private bool interruptRequest;
        private readonly List<(string BusId, string ReaderId)> lastReaders = new List<(string, string)>();

        public TexturedRasterizer(string DeviceName, string DeviceID, Bus host, Bus list, Bus texture, Bus video, Framebuffer screen, DepthBuffer depth, MemoryModule memory, MemoryModule textureMemory, int size = DefaultSize)
        {
            if (size < 2 || size > MaxSize || (size & (size - 1)) != 0) throw new ArgumentException($"A textured rasterizer's texture size is a power of two from 2 to {MaxSize}, not {size}.");
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.host = host;
            this.list = list;
            this.texture = texture;
            this.video = video;
            this.screen = screen ?? throw new ArgumentException("A textured rasterizer needs a framebuffer to draw on.");
            this.memory = memory ?? throw new ArgumentException("A textured rasterizer needs a memory to read its triangles from.");
            this.textureMemory = textureMemory ?? throw new ArgumentException("A textured rasterizer needs a memory to read its texels from.");
            this.depth = depth;
            Size = size;
            address = new Step(Kind.Write, list, Target.None, (memory, "loadmar"));
            word = new Step(Kind.Read, list, Target.Word, (memory, "output"));
            texelAddress = new Step(Kind.Write, texture, Target.None, (textureMemory, "loadmar"));
            texelRead = new Step(Kind.Read, texture, Target.Texel, (textureMemory, "output"));
            plot = new Step(Kind.Write, video, Target.None, (screen, "plot"));
            if (depth == null)
            {
                column = new Step(Kind.Write, video, Target.None, (screen, "loadx"));
                row = new Step(Kind.Write, video, Target.None, (screen, "loady"));
            }
            else
            {
                column = new Step(Kind.Write, video, Target.None, (screen, "loadx"), (depth, "loadx"));
                row = new Step(Kind.Write, video, Target.None, (screen, "loady"), (depth, "loady"));
                depthRead = new Step(Kind.Read, video, Target.Depth, (depth, "output"));
                depthWrite = new Step(Kind.Write, video, Target.None, (depth, "load"), (depth, "next"));
                pass = new Step(Kind.Lines, null, Target.None, (depth, "next"), (screen, "skip"));
            }
            ControlLines
                .Add("loadaddr", loads.Add(() => ListAddress = host.Data))
                .Add("loadcount", loads.Add(() => Count = host.Data))
                .Add("start", () => start = true)
                .Add("status", () => status = true);
        }

        public IEnumerable<(string BusId, string ReaderId)> LastReaders { get { return lastReaders; } }

        public bool TakeInterruptRequest()
        {
            var taken = interruptRequest;
            interruptRequest = false;
            return taken;
        }

        public void Drive()
        {
            lastReaders.Clear();
            if (status)
            {
                host.Data = Busy ? 1 : 0;
                status = false;
            }
            executing = pending;
            pending = null;
            if (executing == null) return;
            foreach (var step in executing) DriveStep(step);
        }

        private void DriveStep(Step step)
        {
            switch (step.Kind)
            {
                case Kind.Write:
                    step.Bus.Data = step.Value;
                    Enable(step.Lines);
                    foreach (var reader in step.Readers) lastReaders.Add((step.Bus.ID, reader));
                    break;
                case Kind.Lines:
                    Enable(step.Lines);
                    break;
                case Kind.Read:
                    lastReaders.Add((step.Bus.ID, deviceID));
                    break;
            }
        }

        public void Latch()
        {
            if (executing != null)
            {
                foreach (var step in executing)
                {
                    if (step.Kind == Kind.Read) Store(step.Target, step.Bus.Data);
                }
            }
            executing = null;
            loads.Latch();
            if (start && !Busy && Count > 0)
            {
                Busy = true;
                job = Job().GetEnumerator();
            }
            start = false;
            if (Busy && pending == null) Advance();
        }

        private void Store(Target target, int value)
        {
            if (target == Target.Word) wordValue = value;
            else if (target == Target.Depth) depthValue = value;
            else if (target == Target.Texel) texelValue = value;
        }

        private void Advance()
        {
            if (job.MoveNext())
            {
                pending = job.Current;
                foreach (var step in pending)
                {
                    if (step.Kind == Kind.Read) Enable(step.Lines);
                }
                return;
            }
            job = null;
            Busy = false;
            Stage = "idle";
            JobsDone++;
            interruptRequest = true;
        }

        private static void Enable(Action[] lines)
        {
            foreach (var line in lines) line();
        }

        private static Step[] Tick(params Step[] steps)
        {
            return steps;
        }

        private IEnumerable<Step[]> Job()
        {
            for (Triangle = 0; Triangle < Count; Triangle++)
            {
                Stage = "reading";
                var words = new int[WordsPerTriangle];
                int start = ListAddress + Triangle * WordsPerTriangle;
                for (int i = 0; i < WordsPerTriangle; i++)
                {
                    yield return Tick(address.With(start + i));
                    yield return Tick(word);
                    words[i] = wordValue;
                }
                Stage = "drawing";
                foreach (var tick in Draw(words)) yield return tick;
                TrianglesDrawn++;
            }
        }

        private IEnumerable<Step[]> Draw(int[] words)
        {
            var triangle = TexturedTriangle.From(words, Size);
            if (triangle == null) yield break;
            for (int y = triangle.Top; y <= triangle.Bottom; y++)
            {
                if (!triangle.Span(y, out int first, out int last)) continue;
                yield return Tick(column.With(first));
                yield return Tick(row.With(y));
                for (int x = first; x <= last; x++)
                {
                    var (texel, shade, z) = triangle.Fragment(x, y);
                    if (depth != null)
                    {
                        yield return Tick(depthRead, texelAddress.With(texel));
                        if (z >= depthValue)
                        {
                            yield return Tick(pass);
                            PixelsHidden++;
                            continue;
                        }
                        yield return Tick(depthWrite.With(z), texelRead);
                    }
                    else
                    {
                        yield return Tick(texelAddress.With(texel));
                        yield return Tick(texelRead);
                    }
                    TexelsRead++;
                    yield return Tick(plot.With(TexturedTriangle.Modulate(texelValue, shade)));
                    PixelsDrawn++;
                }
            }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return status; }

        internal sealed class TexturedTriangle
        {
            private readonly TriangleSetup setup;
            private readonly int[] u = new int[3], v = new int[3], z = new int[3];
            private readonly int textureAddress, size;
            private readonly bool perspective;

            public int Top { get { return setup.Top; } }
            public int Bottom { get { return setup.Bottom; } }

            private TexturedTriangle(TriangleSetup setup, int[] words, int size)
            {
                this.setup = setup;
                this.size = size;
                for (int i = 0; i < 3; i++)
                {
                    z[i] = words[i * WordsPerCorner + 2];
                    u[i] = (short)words[i * WordsPerCorner + 3];
                    v[i] = (short)words[i * WordsPerCorner + 4];
                }
                textureAddress = words[WordsPerTriangle - 1];
                perspective = z[0] > 0 && z[1] > 0 && z[2] > 0;
            }

            public static TexturedTriangle From(int[] words, int size)
            {
                var plain = new int[12];
                for (int i = 0; i < 3; i++)
                {
                    plain[i * 4] = words[i * WordsPerCorner];
                    plain[i * 4 + 1] = words[i * WordsPerCorner + 1];
                    plain[i * 4 + 2] = words[i * WordsPerCorner + 2];
                    plain[i * 4 + 3] = words[i * WordsPerCorner + 5];
                }
                var setup = TriangleSetup.From(plain);
                return setup == null ? null : new TexturedTriangle(setup, words, size);
            }

            public bool Span(int y, out int first, out int last)
            {
                return setup.Span(y, out first, out last);
            }

            public (int Texel, int Shade, int Z) Fragment(int x, int y)
            {
                var (shade, depthAtPixel) = setup.Fragment(x, y);
                var (w0, w1, w2, area) = setup.Weights(x, y);
                var weights = perspective
                    ? new[] { w0 * PerspectiveScale / z[0], w1 * PerspectiveScale / z[1], w2 * PerspectiveScale / z[2] }
                    : new[] { w0, w1, w2 };
                long total = perspective ? weights[0] + weights[1] + weights[2] : area;
                if (total == 0) total = 1;
                int texelU = Interpolate(u, weights, total);
                int texelV = Interpolate(v, weights, total);
                int mask = size - 1;
                int texel = (textureAddress + (texelV & mask) * size + (texelU & mask)) & Bus.Mask;
                return (texel, shade, depthAtPixel);
            }

            private static int Interpolate(int[] values, long[] weights, long total)
            {
                long sum = values[0] * 256L * weights[0] + values[1] * 256L * weights[1] + values[2] * 256L * weights[2];
                return (int)(FloorDivide(sum, total) >> 8);
            }

            private static long FloorDivide(long numerator, long denominator)
            {
                long quotient = numerator / denominator;
                bool roundedUp = numerator % denominator != 0 && (numerator < 0) != (denominator < 0);
                return roundedUp ? quotient - 1 : quotient;
            }

            public static int Modulate(int texel, int shade)
            {
                int r = ((texel >> 11) & 0x1F) * ((shade >> 11) & 0x1F) / 0x1F;
                int g = ((texel >> 5) & 0x3F) * ((shade >> 5) & 0x3F) / 0x3F;
                int b = (texel & 0x1F) * (shade & 0x1F) / 0x1F;
                return (r << 11) | (g << 5) | b;
            }
        }
    }
}
