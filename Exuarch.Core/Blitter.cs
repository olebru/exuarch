using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A device that drives other devices' control lines by itself, without microcode. The machine asks it which
    // devices took a value from which bus in the last tick, so those transfers can be shown like any other.
    public interface IBusMaster
    {
        IEnumerable<(string BusId, string ReaderId)> LastReaders { get; }
    }

    // A graphics coprocessor. The CPU gives it a rectangle on the host bus (loadx, loady, loadw, loadh, loadcolour)
    // and starts it; from then on the blitter fills the rectangle by itself, one bus transfer per tick on its own
    // video bus: the column, then the row, then one pixel per tick along the row, and so on for each row. It does
    // this by putting the value on the video bus and enabling the framebuffer's loadx, loady or plot line, as
    // microcode would. The CPU carries on with its own program and can read status (1 busy, 0 idle) on the host bus.
    // There is no clipping: a rectangle past the right edge wraps like the framebuffer's cursor does.
    public class Blitter : ControlLineDevice, IBusDevice, IBusMaster, IInterruptSource, IObservableState
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Colour { get; private set; }
        public bool Busy { get; private set; }
        // Progress through the current (or last) job.
        public int Row { get; private set; }
        public int Column { get; private set; }
        public long PixelsDrawn { get; private set; }
        public long JobsDone { get; private set; }

        private enum Phase { Column, Row, Pixels }
        private readonly Bus host;
        private readonly Bus video;
        private readonly Framebuffer screen;
        private readonly string deviceID;
        private readonly string deviceName;
        private Phase phase;
        private bool start, status;
        // loadx, loady, loadw, loadh and loadcolour take the host bus value at the end of the tick.
        private readonly LatchedLines loads = new LatchedLines();
        // The screen's lines the blitter drives, bound once.
        private readonly Action screenLoadX, screenLoadY, screenPlot;
        private string lastReader;
        private bool interruptRequest;
        // The job ended in this tick's drive half; the interrupt is asked for in the latch half, like every other
        // source, so whether the controller sees it this tick does not depend on the order of the devices.
        private bool finished;

        // Asks for an interrupt when a job is finished.
        public bool TakeInterruptRequest()
        {
            var taken = interruptRequest;
            interruptRequest = false;
            return taken;
        }

        public Blitter(string DeviceName, string DeviceID, Bus host, Bus video, Framebuffer screen)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.host = host;
            this.video = video;
            this.screen = screen ?? throw new ArgumentException("A blitter needs a framebuffer to draw on.");
            screenLoadX = ControlLineTable.Bind(screen, "loadx");
            screenLoadY = ControlLineTable.Bind(screen, "loady");
            screenPlot = ControlLineTable.Bind(screen, "plot");
            ControlLines
                .Add("loadx", loads.Add(() => X = host.Data))
                .Add("loady", loads.Add(() => Y = host.Data))
                .Add("loadw", loads.Add(() => Width = host.Data))
                .Add("loadh", loads.Add(() => Height = host.Data))
                .Add("loadcolour", loads.Add(() => Colour = host.Data))
                .Add("start", () => start = true)
                .Add("status", () => status = true);
        }

        public IEnumerable<(string BusId, string ReaderId)> LastReaders
        {
            get { if (lastReader != null) yield return (video.ID, lastReader); }
        }

        public void Drive()
        {
            lastReader = null;
            if (status)
            {
                host.Data = Busy ? 1 : 0;
                status = false;
            }
            if (!Busy) return;
            switch (phase)
            {
                case Phase.Column:
                    video.Data = X;
                    screenLoadX();
                    phase = Phase.Row;
                    break;
                case Phase.Row:
                    video.Data = Y + Row;
                    screenLoadY();
                    Column = 0;
                    phase = Phase.Pixels;
                    break;
                case Phase.Pixels:
                    video.Data = Colour;
                    screenPlot();
                    PixelsDrawn++;
                    if (++Column == Width)
                    {
                        phase = Phase.Column;
                        if (++Row == Height)
                        {
                            Busy = false;
                            JobsDone++;
                            finished = true;
                        }
                    }
                    break;
            }
            lastReader = screen.ID();
        }
        public void Latch()
        {
            interruptRequest |= finished;
            finished = false;
            loads.Latch();
            if (start) Start();
            start = false;
        }
        // Like the rasterizer, a start while busy is ignored: wait for status to read 0 first.
        private void Start()
        {
            if (Busy || Width <= 0 || Height <= 0) return;
            Busy = true;
            Row = 0;
            Column = 0;
            phase = Phase.Column;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return status; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".busy", () => Busy ? 1 : 0);
            watch(deviceID + ".row", () => Row);
        }
    }
}
