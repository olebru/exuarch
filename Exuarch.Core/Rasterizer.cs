using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // A triangle rasterizer: the heart of a fixed function GPU. The CPU puts triangles in the rasterizer's memory,
    // gives it their address and how many there are (loadaddr, loadcount) and starts it. From then on it works by
    // itself, one bus transfer per tick, while the CPU carries on:
    //
    //   1. It reads each triangle from its memory over its list bus, 12 words: x, y, z and colour for each of the
    //      three corners. A word takes two ticks, one to put the address in the memory's MAR, one to read the cell.
    //   2. It fills the triangle row by row. A row starts with two ticks that put the column and the row in the
    //      framebuffer's cursor (and the depth buffer's). Then each pixel is one plot: the colour, blended from the
    //      three corner colours by how near the pixel is to each corner (Gouraud shading; three equal colours give a
    //      flat triangle).
    //   3. With a depth buffer connected, each pixel is first read from the depth buffer (one tick). Only if the
    //      triangle is nearer there (a smaller z) is the new depth written and the colour plotted (two more ticks);
    //      otherwise both cursors step past the pixel (one tick). So hidden surfaces cost less than drawn ones, but
    //      the depth test still costs a read for every pixel: memory bandwidth, the thing GPUs are built around.
    //
    // Pixels whose centre is inside the triangle are drawn; a pixel exactly on an edge shared by two triangles is
    // drawn by one of them only (the top-left rule), so a mesh has no gaps and no pixel drawn twice. Pixels off the
    // screen are not drawn. status puts 1 on the host bus while busy; it asks for an interrupt when it is done.
    public class Rasterizer : ControlLineDevice, IBusDevice, IBusMaster, IInterruptSource
    {
        public const int WordsPerTriangle = 12;

        public int ListAddress { get; private set; }
        public int Count { get; private set; }
        public bool Busy { get; private set; }
        // The triangle being read or drawn, counting from 0, and totals since the machine started.
        public int Triangle { get; private set; }
        public string Stage { get; private set; } = "idle";
        public long TrianglesDrawn { get; private set; }
        public long PixelsDrawn { get; private set; }
        public long PixelsHidden { get; private set; }
        public long JobsDone { get; private set; }

        private enum Kind { Write, Read, Lines }
        // One step per kind of transfer, made once and reused with a new value: a step is carried out in the tick
        // after it is chosen, before the next one is chosen, and allocating one per tick is costly where .NET is
        // interpreted (WebAssembly).
        private sealed class Step
        {
            public Kind Kind;
            public Bus Bus;
            public int Value;
            // The lines the step enables, bound once, and the devices they belong to.
            public Action[] Lines;
            public string[] Readers;

            public Step(Kind kind, Bus bus, params (IBusDevice Device, string Line)[] lines)
            {
                Kind = kind;
                Bus = bus;
                Lines = lines.Select(l => ControlLineTable.Bind(l.Device, l.Line)).ToArray();
                Readers = lines.Select(l => l.Device.ID()).Distinct().ToArray();
            }
            public Step With(int value)
            {
                Value = value;
                return this;
            }
        }
        private readonly Step address, word, column, row, plot, depthRead, depthWrite, pass;

        private readonly Bus host, list, video;
        private readonly Framebuffer screen;
        private readonly DepthBuffer depth;
        private readonly MemoryModule memory;
        private readonly string deviceID, deviceName;
        private bool start, status;
        // loadaddr and loadcount take the host bus value at the end of the tick.
        private readonly LatchedLines loads = new LatchedLines();
        private IEnumerator<Step> job;
        private Step pending, executing;
        private int readValue;
        private bool interruptRequest;
        private readonly List<(string BusId, string ReaderId)> lastReaders = new List<(string, string)>();

        public Rasterizer(string DeviceName, string DeviceID, Bus host, Bus list, Bus video, Framebuffer screen, DepthBuffer depth, MemoryModule memory)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.host = host;
            this.list = list;
            this.video = video;
            this.screen = screen ?? throw new ArgumentException("A rasterizer needs a framebuffer to draw on.");
            this.memory = memory ?? throw new ArgumentException("A rasterizer needs a memory to read its triangles from.");
            this.depth = depth;
            address = new Step(Kind.Write, list, (memory, "loadmar"));
            word = new Step(Kind.Read, list, (memory, "output"));
            plot = new Step(Kind.Write, video, (screen, "plot"));
            if (depth == null)
            {
                column = new Step(Kind.Write, video, (screen, "loadx"));
                row = new Step(Kind.Write, video, (screen, "loady"));
            }
            else
            {
                column = new Step(Kind.Write, video, (screen, "loadx"), (depth, "loadx"));
                row = new Step(Kind.Write, video, (screen, "loady"), (depth, "loady"));
                depthRead = new Step(Kind.Read, video, (depth, "output"));
                depthWrite = new Step(Kind.Write, video, (depth, "load"), (depth, "next"));
                pass = new Step(Kind.Lines, null, (depth, "next"), (screen, "skip"));
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
            switch (executing.Kind)
            {
                case Kind.Write:
                    executing.Bus.Data = executing.Value;
                    Enable(executing.Lines);
                    foreach (var reader in executing.Readers) lastReaders.Add((executing.Bus.ID, reader));
                    break;
                case Kind.Lines:
                    Enable(executing.Lines);
                    break;
                case Kind.Read:
                    // The source was told to drive in the last tick's latch, so it does so now whatever the device order.
                    lastReaders.Add((executing.Bus.ID, deviceID));
                    break;
            }
        }

        public void Latch()
        {
            if (executing?.Kind == Kind.Read) readValue = executing.Bus.Data;
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

        // The next tick's step, or the end of the job.
        private void Advance()
        {
            if (job.MoveNext())
            {
                pending = job.Current;
                if (pending.Kind == Kind.Read) Enable(pending.Lines);
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

        private IEnumerable<Step> Job()
        {
            for (Triangle = 0; Triangle < Count; Triangle++)
            {
                Stage = "reading";
                var words = new int[WordsPerTriangle];
                int address = ListAddress + Triangle * WordsPerTriangle;
                for (int i = 0; i < WordsPerTriangle; i++)
                {
                    yield return this.address.With(address + i);
                    yield return word;
                    words[i] = readValue;
                }
                Stage = "drawing";
                foreach (var step in Draw(words)) yield return step;
                TrianglesDrawn++;
            }
        }

        // Row by row: a row starts by putting its first column and the row in the cursors, then each pixel is
        // plotted. With a depth buffer, each pixel is read from it first, and drawn only where the triangle is nearer.
        private IEnumerable<Step> Draw(int[] words)
        {
            var triangle = TriangleSetup.From(words);
            if (triangle == null) yield break;
            for (int y = triangle.Top; y <= triangle.Bottom; y++)
            {
                if (!triangle.Span(y, out int first, out int last)) continue;
                yield return column.With(first);
                yield return row.With(y);
                for (int x = first; x <= last; x++)
                {
                    var (colour, z) = triangle.Fragment(x, y);
                    if (depth != null)
                    {
                        yield return depthRead;
                        if (z >= readValue)
                        {
                            yield return pass;
                            PixelsHidden++;
                            continue;
                        }
                        yield return depthWrite.With(z);
                    }
                    yield return plot.With(colour);
                    PixelsDrawn++;
                }
            }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return status; }
    }
}
