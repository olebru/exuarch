using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class ColumnBlitter : ControlLineDevice, IBusDevice, IBusMaster, IInterruptSource, IObservableState
    {
        public const int DefaultQueueSize = 4;
        public const int MaxQueueSize = 64;
        public const int TexelBufferSize = 2;
        public const int FractionBits = 8;
        public const int NoKey = -1;
        private const int UnknownAddress = -1;

        public sealed record Job(int X, int Y, int Width, int Height, int Source, int Start, int Step, int Key, bool IsSwap = false)
        {
            public int Pixels { get { return IsSwap ? 0 : Width * Height; } }
        }

        public sealed class TexelCounter
        {
            public Job Job { get; private set; }
            public int Column { get; private set; }
            public int Pixel { get; private set; }
            public long Position { get; private set; }
            public bool ReadsTexel { get; private set; }

            public void Begin(Job job)
            {
                Job = job;
                Column = 0;
                Pixel = 0;
                Position = job.Start;
                ReadsTexel = true;
            }

            public bool Follows(Job job)
            {
                return ReferenceEquals(Job, job);
            }

            public bool Done { get { return Job.IsSwap || Column == Job.Width; } }

            public int Address { get { return (int)((Job.Source + (Position >> FractionBits)) & Bus.Mask); } }

            public void Advance()
            {
                if (++Pixel == Job.Height)
                {
                    Pixel = 0;
                    Column++;
                    Position = Job.Start;
                    ReadsTexel = true;
                    return;
                }
                long next = Position + Job.Step;
                ReadsTexel = (next >> FractionBits) != (Position >> FractionBits);
                Position = next;
            }
        }

        private enum PlotPhase { Column, Row, Pixels }

        public int X { get; private set; }
        public int Y { get; private set; }
        public int Width { get; private set; } = 1;
        public int Height { get; private set; }
        public int Source { get; private set; }
        public int Start { get; private set; }
        public int Step { get; private set; }
        public int Key { get; private set; } = NoKey;
        public int QueueSize { get; }
        public IReadOnlyList<Job> Jobs { get { return jobs; } }
        public bool Busy { get { return jobs.Count > 0; } }
        public int Column { get { return plot.Column; } }
        public int Pixel { get { return plot.Pixel; } }
        public long PixelsDrawn { get; private set; }
        public long PixelsSkipped { get; private set; }
        public long TexelsRead { get; private set; }
        public long AddressesSent { get; private set; }
        public long Stalls { get; private set; }
        public long JobsDone { get; private set; }
        public long Dropped { get; private set; }
        public long Swaps { get; private set; }

        private readonly Bus host, texture, video;
        private readonly Framebuffer screen;
        private readonly MemoryModule memory;
        private readonly string deviceID, deviceName;
        private readonly Action screenLoadX, screenLoadY, screenPlotDown, screenSkipDown, screenSwap, memoryLoadMar, memoryOutput, memoryIncMar;
        private readonly LatchedLines loads = new LatchedLines();
        private readonly List<Job> jobs = new List<Job>();
        private readonly Queue<int> texels = new Queue<int>(TexelBufferSize);
        private readonly List<(string BusId, string ReaderId)> lastReaders = new List<(string, string)>();
        private readonly TexelCounter plot = new TexelCounter();
        private readonly TexelCounter fetch = new TexelCounter();
        private PlotPhase plotPhase;
        private int currentTexel;
        private bool jobFinishedThisTick;
        private int fetchJob;
        private int addressSentThisTick = UnknownAddress;
        private bool readThisTick;
        private int memoryAddress = UnknownAddress;
        private bool push, swap, status, interruptRequest;

        public ColumnBlitter(string DeviceName, string DeviceID, Bus host, Bus texture, Bus video, Framebuffer screen, MemoryModule memory, int queueSize = DefaultQueueSize)
        {
            if (queueSize < 1 || queueSize > MaxQueueSize) throw new ArgumentException($"A column blitter's queue holds 1 to {MaxQueueSize} jobs, not {queueSize}.");
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.host = host;
            this.texture = texture;
            this.video = video;
            this.screen = screen ?? throw new ArgumentException("A column blitter needs a framebuffer to draw on.");
            this.memory = memory ?? throw new ArgumentException("A column blitter needs a memory to read its texels from.");
            QueueSize = queueSize;
            screenLoadX = ControlLineTable.Bind(screen, "loadx");
            screenLoadY = ControlLineTable.Bind(screen, "loady");
            screenPlotDown = ControlLineTable.Bind(screen, "plotdown");
            screenSkipDown = ControlLineTable.Bind(screen, "skipdown");
            screenSwap = screen is DoubleFramebuffer ? ControlLineTable.Bind(screen, "swap") : null;
            memoryLoadMar = ControlLineTable.Bind(memory, "loadmar");
            memoryOutput = ControlLineTable.Bind(memory, "output");
            memoryIncMar = ControlLineTable.Bind(memory, "incmar");
            ControlLines
                .Add("loadx", loads.Add(() => X = host.Data))
                .Add("loady", loads.Add(() => Y = host.Data))
                .Add("loadw", loads.Add(() => Width = host.Data))
                .Add("loadh", loads.Add(() => Height = host.Data))
                .Add("loadsrc", loads.Add(() => Source = host.Data))
                .Add("loadstart", loads.Add(() => Start = host.Data))
                .Add("loadstep", loads.Add(() => Step = host.Data))
                .Add("loadkey", loads.Add(() => Key = host.Data))
                .Add("nokey", loads.Add(() => Key = NoKey))
                .Add("push", () => push = true)
                .Add("swap", () => swap = true)
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
                host.Data = jobs.Count;
                status = false;
            }
            DrivePlot();
            DriveFetch();
        }

        private void DrivePlot()
        {
            if (jobs.Count == 0) return;
            var job = jobs[0];
            if (!plot.Follows(job)) plot.Begin(job);
            if (job.IsSwap)
            {
                screenSwap?.Invoke();
                Swaps++;
                jobFinishedThisTick = true;
                return;
            }
            switch (plotPhase)
            {
                case PlotPhase.Column:
                    video.Data = (job.X + plot.Column) & Bus.Mask;
                    screenLoadX();
                    lastReaders.Add((video.ID, screen.ID()));
                    plotPhase = PlotPhase.Row;
                    break;
                case PlotPhase.Row:
                    video.Data = job.Y;
                    screenLoadY();
                    lastReaders.Add((video.ID, screen.ID()));
                    plotPhase = PlotPhase.Pixels;
                    break;
                case PlotPhase.Pixels:
                    DrivePixel(job);
                    break;
            }
        }

        private void DrivePixel(Job job)
        {
            if (plot.ReadsTexel)
            {
                if (texels.Count == 0)
                {
                    Stalls++;
                    return;
                }
                currentTexel = texels.Dequeue();
            }
            if (currentTexel == job.Key)
            {
                screenSkipDown();
                PixelsSkipped++;
            }
            else
            {
                video.Data = currentTexel;
                screenPlotDown();
                lastReaders.Add((video.ID, screen.ID()));
                PixelsDrawn++;
            }
            plot.Advance();
            if (plot.Done) jobFinishedThisTick = true;
            else if (plot.Pixel == 0) plotPhase = PlotPhase.Column;
        }

        private void DriveFetch()
        {
            if (readThisTick)
            {
                memoryIncMar();
                lastReaders.Add((texture.ID, deviceID));
                return;
            }
            if (texels.Count >= TexelBufferSize || !NextRead(out int address)) return;
            texture.Data = address;
            memoryLoadMar();
            lastReaders.Add((texture.ID, memory.ID()));
            addressSentThisTick = address % memory.Size;
            AddressesSent++;
        }

        public void Latch()
        {
            if (readThisTick)
            {
                texels.Enqueue(texture.Data);
                TexelsRead++;
                memoryAddress = (memoryAddress + 1) % memory.Size;
                readThisTick = false;
            }
            if (addressSentThisTick != UnknownAddress)
            {
                memoryAddress = addressSentThisTick;
                addressSentThisTick = UnknownAddress;
                StartRead();
            }
            if (jobFinishedThisTick) FinishJob();
            loads.Latch();
            if (push) Push();
            if (swap) Queue(new Job(0, 0, 0, 0, 0, 0, 0, NoKey, IsSwap: true));
            push = swap = false;
            if (!readThisTick && texels.Count < TexelBufferSize && NextRead(out int next) && next % memory.Size == memoryAddress) StartRead();
        }

        private void StartRead()
        {
            memoryOutput();
            readThisTick = true;
            fetch.Advance();
        }

        private bool NextRead(out int address)
        {
            while (fetchJob < jobs.Count)
            {
                var job = jobs[fetchJob];
                if (!fetch.Follows(job)) fetch.Begin(job);
                while (!fetch.Done)
                {
                    if (fetch.ReadsTexel)
                    {
                        address = fetch.Address;
                        return true;
                    }
                    fetch.Advance();
                }
                fetchJob++;
            }
            address = UnknownAddress;
            return false;
        }

        private void FinishJob()
        {
            jobFinishedThisTick = false;
            jobs.RemoveAt(0);
            if (fetchJob > 0) fetchJob--;
            plotPhase = PlotPhase.Column;
            JobsDone++;
            if (jobs.Count > 0) return;
            interruptRequest = true;
            memoryAddress = UnknownAddress;
        }

        private void Push()
        {
            if (Height == 0 || Width == 0) return;
            Queue(new Job(X, Y, Width, Height, Source, Start, Step, Key));
        }

        private void Queue(Job job)
        {
            if (jobs.Count == QueueSize)
            {
                Dropped++;
                return;
            }
            jobs.Add(job);
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return status; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".queued", () => jobs.Count);
            watch(deviceID + ".pixel", () => plot.Pixel);
        }
    }
}
