using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ColumnBlitterTests
{
    private const int TextureBase = 100;
    private readonly Bus host = new Bus("host");
    private readonly Bus texture = new Bus("texture");
    private readonly Bus video = new Bus("video");
    private readonly Register cpu;
    private readonly Framebuffer screen;
    private readonly RamModule texels;
    private readonly ColumnBlitter blit;

    public ColumnBlitterTests()
    {
        cpu = new Register("CPU", "cpu", host);
        screen = new Framebuffer("FB", "fb", video);
        texels = new RamModule("TEX", "tex", texture, 1024);
        texels.LoadProgram(Enumerable.Range(0, TextureBase + 64).Select(i => i < TextureBase ? 0 : 1000 + i - TextureBase).ToArray());
        blit = new ColumnBlitter("COLS", "cols", host, texture, video, screen, texels);
    }

    private void Tick() => Clocking.Tick(new[] { host, texture, video }, new IBusDevice[] { cpu, blit, texels, screen });

    private void Load(string line, int value)
    {
        cpu.Data = value;
        cpu.Enable("output");
        blit.Enable(line);
        Tick();
    }

    private void Push(int x, int y, int height, int step, int start = 0, int source = TextureBase)
    {
        Load("loadx", x);
        Load("loady", y);
        Load("loadh", height);
        Load("loadsrc", source);
        Load("loadstart", start);
        Load("loadstep", step);
        blit.Enable("push");
        Tick();
    }

    private int Status()
    {
        blit.Enable("status");
        cpu.Enable("load");
        Tick();
        return cpu.Data;
    }

    private int RunUntilIdle()
    {
        int ticks = 0;
        while (blit.Busy)
        {
            Tick();
            ticks++;
            Assert.True(ticks < 10_000, "the queue never emptied");
        }
        return ticks;
    }

    private int Pixel(int x, int y) => screen.Pixels[y * Framebuffer.Width + x];

    [Fact]
    public void LinesMatchTheRegistry()
    {
        var info = DeviceRegistry.CreateDefault().Info("columnBlitter");
        Assert.Equal(blit.SignalLines(), info.ControlLines.Select(l => l.Name));
        Assert.Equal(new[] { "host", "tex", "video" }, info.Ports);
        Assert.Equal(new[] { "screen", "memory" }, info.Connections.Select(c => c.Name));
    }

    [Fact]
    public void AColumnAtOneTexelPerPixelStreamsOnePixelPerTick()
    {
        Push(10, 20, 8, 256);
        int ticks = RunUntilIdle();
        Assert.Equal(Enumerable.Range(1000, 8), Enumerable.Range(20, 8).Select(y => Pixel(10, y)));
        Assert.Equal(0, Pixel(10, 28));
        Assert.Equal(0, Pixel(11, 20));
        Assert.Equal(2 + 8, ticks);
        Assert.Equal(8, blit.TexelsRead);
        Assert.Equal(1, blit.AddressesSent);
        Assert.Equal(1, blit.JobsDone);
    }

    [Fact]
    public void AMagnifiedColumnReadsEachTexelOnce()
    {
        Push(0, 0, 12, 64);
        int ticks = RunUntilIdle();
        Assert.Equal(Enumerable.Range(0, 12).Select(y => 1000 + y / 4), Enumerable.Range(0, 12).Select(y => Pixel(0, y)));
        Assert.Equal(3, blit.TexelsRead);
        Assert.Equal(2 + 12, ticks);
    }

    [Fact]
    public void AShrunkColumnSendsAnAddressForEveryTexelItSkipsTo()
    {
        Push(5, 5, 4, 512);
        int ticks = RunUntilIdle();
        Assert.Equal(new[] { 1000, 1002, 1004, 1006 }, Enumerable.Range(5, 4).Select(y => Pixel(5, y)));
        Assert.Equal(4, blit.AddressesSent);
        Assert.InRange(ticks, 8, 10);
    }

    [Fact]
    public void AWideJobDrawsTheSameTextureColumnSideBySide()
    {
        Load("loadw", 3);
        Push(20, 4, 5, 256);
        int ticks = RunUntilIdle();
        for (int x = 20; x < 23; x++) Assert.Equal(Enumerable.Range(1000, 5), Enumerable.Range(4, 5).Select(y => Pixel(x, y)));
        Assert.Equal(0, Pixel(23, 4));
        Assert.Equal(3 * (2 + 5), ticks);
        Assert.Equal(1, blit.JobsDone);
        Load("loadw", 0);
        Push(20, 4, 5, 256);
        Assert.False(blit.Busy);
    }

    [Fact]
    public void ASwapJobShowsTheFrameWhenTheColumnsBeforeItAreDrawn()
    {
        var pages = new DoubleFramebuffer("PAGES", "pages", video);
        var flipper = new ColumnBlitter("COLS", "cols3", host, texture, video, pages, texels);
        void TickFlipper() => Clocking.Tick(new[] { host, texture, video }, new IBusDevice[] { cpu, flipper, texels, pages });
        void LoadFlipper(string line, int value)
        {
            cpu.Data = value;
            cpu.Enable("output");
            flipper.Enable(line);
            TickFlipper();
        }
        LoadFlipper("loadx", 6);
        LoadFlipper("loady", 0);
        LoadFlipper("loadh", 10);
        LoadFlipper("loadsrc", TextureBase);
        LoadFlipper("loadstep", 256);
        flipper.Enable("push");
        TickFlipper();
        flipper.Enable("swap");
        TickFlipper();
        Assert.Equal(2, flipper.Jobs.Count);
        Assert.Equal(0, pages.Swaps);
        while (flipper.Busy) TickFlipper();
        Assert.Equal(1, pages.Swaps);
        Assert.Equal(1, flipper.Swaps);
        Assert.Equal(1009, pages.Shown[9 * Framebuffer.Width + 6]);
        Assert.True(flipper.TakeInterruptRequest());
        blit.Enable("swap");
        Tick();
        RunUntilIdle();
        Assert.Equal(1, blit.Swaps);
    }

    [Fact]
    public void TheStartPositionPicksTheFirstTexel()
    {
        Push(1, 1, 3, 256, start: 10 * 256 + 128);
        RunUntilIdle();
        Assert.Equal(new[] { 1010, 1011, 1012 }, Enumerable.Range(1, 3).Select(y => Pixel(1, y)));
    }

    [Fact]
    public void AStepOfZeroFillsTheColumnWithOneTexel()
    {
        Push(3, 0, 50, 0, source: TextureBase + 7);
        RunUntilIdle();
        Assert.All(Enumerable.Range(0, 50), y => Assert.Equal(1007, Pixel(3, y)));
        Assert.Equal(1, blit.TexelsRead);
    }

    [Fact]
    public void TexelsEqualToTheKeyLeaveTheScreenAsItWas()
    {
        screen.Pixels[2 * Framebuffer.Width + 4] = 0x1234;
        texels.LoadProgram(Enumerable.Range(0, TextureBase + 3).Select(i => i == TextureBase + 1 ? 0xF81F : i).ToArray());
        Load("loadkey", 0xF81F);
        Push(4, 1, 3, 256);
        RunUntilIdle();
        Assert.Equal(new[] { TextureBase, 0x1234, TextureBase + 2 }, Enumerable.Range(1, 3).Select(y => Pixel(4, y)));
        Assert.Equal(1, blit.PixelsSkipped);
        blit.Enable("nokey");
        Tick();
        Push(4, 1, 3, 256);
        RunUntilIdle();
        Assert.Equal(0xF81F, Pixel(4, 2));
    }

    [Fact]
    public void QueuedColumnsFollowEachOtherWithTwoTicksBetween()
    {
        var other = new ColumnBlitter("COLS", "cols2", host, texture, video, screen, texels, queueSize: 3);
        void TickBoth() => Clocking.Tick(new[] { host, texture, video }, new IBusDevice[] { cpu, other, texels, screen });
        void LoadOther(string line, int value)
        {
            cpu.Data = value;
            cpu.Enable("output");
            other.Enable(line);
            TickBoth();
        }
        LoadOther("loadh", 16);
        LoadOther("loadsrc", TextureBase);
        LoadOther("loadstep", 256);
        for (int x = 0; x < 4; x++)
        {
            LoadOther("loadx", x);
            other.Enable("push");
            TickBoth();
        }
        Assert.Equal(3, other.Jobs.Count);
        Assert.Equal(1, other.Dropped);
        int ticks = 0;
        while (other.Busy) { TickBoth(); ticks++; }
        Assert.InRange(ticks, 3 * 16, 3 * 18 + 2);
        for (int x = 0; x < 3; x++) Assert.Equal(1015, Pixel(x, 15));
        Assert.Equal(0, Pixel(3, 15));
    }

    [Fact]
    public void StatusCountsTheQueueAndAnInterruptMarksItEmpty()
    {
        Assert.Equal(0, Status());
        Push(0, 0, 30, 256);
        Push(1, 0, 30, 256);
        Assert.Equal(2, Status());
        Assert.False(blit.TakeInterruptRequest());
        RunUntilIdle();
        Assert.True(blit.TakeInterruptRequest());
        Assert.Equal(0, Status());
        Assert.Equal(2, blit.JobsDone);
    }

    [Fact]
    public void TheHostBusStaysFreeWhileItDraws()
    {
        Push(0, 0, 40, 256);
        var other = new Register("OTHER", "other", host);
        for (int i = 0; i < 20; i++)
        {
            cpu.Data = i;
            cpu.Enable("output");
            other.Enable("load");
            Clocking.Tick(new[] { host, texture, video }, new IBusDevice[] { cpu, other, blit, texels, screen });
            Assert.Equal(i, other.Data);
        }
    }

    [Fact]
    public void AnEmptyColumnIsNotQueuedAndItNeedsAScreenAndAMemory()
    {
        Push(0, 0, 0, 256);
        Assert.False(blit.Busy);
        Assert.Throws<ArgumentException>(() => new ColumnBlitter("C", "c", host, texture, video, null, texels));
        Assert.Throws<ArgumentException>(() => new ColumnBlitter("C", "c", host, texture, video, screen, null));
        Assert.Throws<ArgumentException>(() => new ColumnBlitter("C", "c", host, texture, video, screen, texels, queueSize: 0));
    }

    private sealed class InterruptListener : IBusDevice
    {
        private readonly IInterruptSource source;
        public int Tick, TakenAt = -1;
        public InterruptListener(IInterruptSource source) { this.source = source; }
        public void Drive() { if (source.TakeInterruptRequest() && TakenAt < 0) TakenAt = Tick; }
        public void Latch() { Tick++; }
        public string DisplayName() => "listener";
        public void Enable(string function) { }
        public string ID() => "listener";
        public bool IsOutputEnabled() => false;
        public System.Collections.Generic.List<string> SignalLines() => new System.Collections.Generic.List<string>();
    }

    private static string RunInOrder(int[] order, bool listenerFirst)
    {
        var hostBus = new Bus("host");
        var textureBus = new Bus("texture");
        var videoBus = new Bus("video");
        var source = new Register("CPU", "cpu", hostBus);
        var pages = new DoubleFramebuffer("PAGES", "pages", videoBus);
        var memory = new RamModule("TEX", "tex", textureBus, 1024);
        memory.LoadProgram(Enumerable.Range(0, TextureBase + 64).Select(i => i == TextureBase + 5 ? 0xF81F : i < TextureBase ? 0 : 1000 + i - TextureBase).ToArray());
        var columns = new ColumnBlitter("COLS", "cols", hostBus, textureBus, videoBus, pages, memory);
        var listener = new InterruptListener(columns);
        var parts = new IBusDevice[] { source, columns, memory, pages };
        var ordered = order.Select(i => parts[i]).ToList();
        if (listenerFirst) ordered.Insert(0, listener);
        else ordered.Add(listener);
        var devices = ordered.ToArray();
        void Tick() => Clocking.Tick(new[] { hostBus, textureBus, videoBus }, devices);
        void Load(string line, int value)
        {
            source.Data = value;
            source.Enable("output");
            columns.Enable(line);
            Tick();
        }
        void Push(int x, int y, int width, int height, int step, int start)
        {
            Load("loadx", x);
            Load("loady", y);
            Load("loadw", width);
            Load("loadh", height);
            Load("loadsrc", TextureBase);
            Load("loadstart", start);
            Load("loadstep", step);
            columns.Enable("push");
            Tick();
        }
        Load("loadkey", 0xF81F);
        Push(10, 5, 3, 12, 256, 0);
        Push(20, 0, 1, 20, 96, 128);
        Push(30, 2, 2, 9, 600, 300);
        columns.Enable("swap");
        Tick();
        while (columns.Busy || listener.TakenAt < 0) Tick();
        var hash = new System.Text.StringBuilder();
        foreach (var y in Enumerable.Range(0, 24))
            foreach (var x in new[] { 10, 11, 12, 20, 30, 31 })
                hash.Append(pages.Shown[y * Framebuffer.Width + x]).Append(',');
        return $"{listener.TakenAt} {columns.TexelsRead} {columns.AddressesSent} {columns.Stalls} {columns.PixelsSkipped} {pages.Swaps} {hash}";
    }

    private static System.Collections.Generic.IEnumerable<int[]> Orders(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        foreach (var first in items)
            foreach (var rest in Orders(items.Where(i => i != first).ToArray()))
                yield return new[] { first }.Concat(rest).ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryDeviceOrderDrawsTheSameFrameAndTakesTheInterruptInTheSameTick(bool listenerFirst)
    {
        var orders = Orders(new[] { 0, 1, 2, 3 }).ToList();
        Assert.Equal(24, orders.Count);
        var results = orders.Select(order => RunInOrder(order, listenerFirst)).Distinct().ToList();
        Assert.Single(results);
        Assert.Contains("1009,1009,1009", results[0]);
    }
}
