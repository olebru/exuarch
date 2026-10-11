using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class GpuTests
{
    private sealed class Rig
    {
        public readonly Bus Host = new Bus("host"), List = new Bus("list"), Video = new Bus("video");
        public readonly RamModule Memory;
        public readonly Framebuffer Screen;
        public readonly DepthBuffer Depth;
        public readonly Rasterizer Rasterizer;
        public readonly Register Cpu;
        private readonly Bus[] buses;
        private readonly IBusDevice[] devices;
        public int Ticks;

        public Rig(bool depth = false, bool reversed = false)
        {
            Memory = new RamModule("LIST", "lmem", List, 4096);
            Screen = new Framebuffer("FB", "fb", Video);
            Depth = depth ? new DepthBuffer("Z", "zb", Video) : null;
            Rasterizer = new Rasterizer("R", "rast", Host, List, Video, Screen, Depth, Memory);
            Cpu = new Register("CPU", "cpu", Host);
            buses = new[] { Host, List, Video };
            var all = new List<IBusDevice> { Cpu, Rasterizer, Memory, Screen };
            if (Depth != null) all.Add(Depth);
            if (reversed) all.Reverse();
            devices = all.ToArray();
        }

        public void Tick()
        {
            Clocking.Tick(buses, devices);
            Ticks++;
        }

        public int Draw(int address, int count)
        {
            Cpu.Data = address; Cpu.Enable("output"); Rasterizer.Enable("loadaddr"); Tick();
            Cpu.Data = count; Cpu.Enable("output"); Rasterizer.Enable("loadcount"); Tick();
            Rasterizer.Enable("start"); Tick();
            int start = Ticks;
            while (Rasterizer.Busy) { Tick(); Assert.True(Ticks - start < 5_000_000); }
            return Ticks - start;
        }

        public void Put(int address, params (int X, int Y, int Z, int Colour)[] corners)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                var (x, y, z, colour) = corners[i];
                Memory.memory[address + i * 4] = (ushort)(x & 0xFFFF);
                Memory.memory[address + i * 4 + 1] = (ushort)(y & 0xFFFF);
                Memory.memory[address + i * 4 + 2] = (ushort)z;
                Memory.memory[address + i * 4 + 3] = (ushort)colour;
            }
        }

        public HashSet<(int, int)> Lit()
        {
            var lit = new HashSet<(int, int)>();
            for (int y = 0; y < Framebuffer.Height; y++)
                for (int x = 0; x < Framebuffer.Width; x++)
                    if (Screen.Pixels[y * Framebuffer.Width + x] != 0) lit.Add((x, y));
            return lit;
        }
        public int Pixel(int x, int y) { return Screen.Pixels[y * Framebuffer.Width + x]; }
    }

    private const int Red = 0xF800, Green = 0x07E0, Blue = 0x001F, White = 0xFFFF;

    private static long Edge((long X, long Y) a, (long X, long Y) b, (long X, long Y) p)
    {
        return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    }
    private static HashSet<(int, int)> StrictlyInside(params (int X, int Y)[] polygon)
    {
        var inside = new HashSet<(int, int)>();
        var p = polygon.Select(c => (X: 2L * c.X, Y: 2L * c.Y)).ToArray();
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                var centre = (2L * x + 1, 2L * y + 1);
                if (Enumerable.Range(0, p.Length).All(i => Edge(p[i], p[(i + 1) % p.Length], centre) > 0)) inside.Add((x, y));
            }
        return inside;
    }

    private static HashSet<(int, int)> InsideOrOnEdge(params (int X, int Y)[] polygon)
    {
        var inside = new HashSet<(int, int)>();
        var p = polygon.Select(c => (X: 2L * c.X, Y: 2L * c.Y)).ToArray();
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                var centre = (2L * x + 1, 2L * y + 1);
                if (Enumerable.Range(0, p.Length).All(i => Edge(p[i], p[(i + 1) % p.Length], centre) >= 0)) inside.Add((x, y));
            }
        return inside;
    }

    [Fact]
    public void AFlatTriangleCoversThePixelsWhoseCentreIsInside()
    {
        var rig = new Rig();
        rig.Put(0, (10, 10, 0, White), (60, 20, 0, White), (25, 50, 0, White));
        rig.Draw(0, 1);
        var lit = rig.Lit();
        Assert.Empty(StrictlyInside((10, 10), (60, 20), (25, 50)).Except(lit));
        Assert.Empty(lit.Except(InsideOrOnEdge((10, 10), (60, 20), (25, 50))));
        Assert.All(lit, p => Assert.Equal(White, rig.Pixel(p.Item1, p.Item2)));
        Assert.Equal(lit.Count, rig.Rasterizer.PixelsDrawn);
        Assert.Equal(1, rig.Rasterizer.TrianglesDrawn);
    }

    [Theory]
    [InlineData(0, 0, 10, 0, 10, 10, 0, 10)]
    [InlineData(5, 5, 40, 12, 33, 44, 2, 30)]
    [InlineData(100, 100, 140, 100, 140, 140, 100, 140)]
    [InlineData(20, 20, 60, 21, 59, 61, 19, 60)]
    public void TrianglesSharingAnEdgeLeaveNoGapAndNoOverlap(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
    {
        HashSet<(int, int)> Alone((int, int)[] corners, bool flip)
        {
            var rig = new Rig();
            var order = flip ? new[] { corners[0], corners[2], corners[1] } : corners;
            rig.Put(0, order.Select(c => (c.Item1, c.Item2, 0, White)).ToArray());
            rig.Draw(0, 1);
            return rig.Lit();
        }
        foreach (var flip in new[] { false, true })
        {
            var first = Alone(new[] { (ax, ay), (bx, by), (cx, cy) }, flip);
            var second = Alone(new[] { (ax, ay), (cx, cy), (dx, dy) }, !flip);
            Assert.Empty(first.Intersect(second));
            var quad = StrictlyInside((ax, ay), (bx, by), (cx, cy), (dx, dy));
            Assert.Empty(quad.Except(first.Union(second)));
        }
    }

    [Fact]
    public void APixelOnASharedEdgeBelongsToTheTriangleWhoseLeftEdgeItIs()
    {
        HashSet<(int, int)> Alone(params (int, int)[] corners)
        {
            var rig = new Rig();
            rig.Put(0, corners.Select(c => (c.Item1, c.Item2, 0, White)).ToArray());
            rig.Draw(0, 1);
            return rig.Lit();
        }
        var belowLeft = Alone((0, 0), (10, 10), (0, 10));
        var aboveRight = Alone((0, 0), (10, 0), (10, 10));
        Assert.DoesNotContain((3, 3), belowLeft);
        Assert.Contains((3, 3), aboveRight);
    }

    [Fact]
    public void GouraudShadingBlendsTheCornerColours()
    {
        var rig = new Rig();
        rig.Put(0, (100, 100, 0, Red), (300, 100, 0, Green), (100, 300, 0, Blue));
        rig.Draw(0, 1);
        var (r, g, b) = Framebuffer.ToRgb(rig.Pixel(101, 101));
        Assert.True(r > 240 && g < 16 && b < 16, $"near the red corner: {r},{g},{b}");
        (r, g, b) = Framebuffer.ToRgb(rig.Pixel(297, 101));
        Assert.True(g > 240 && r < 16 && b < 16, $"near the green corner: {r},{g},{b}");
        (r, g, b) = Framebuffer.ToRgb(rig.Pixel(101, 297));
        Assert.True(b > 240 && r < 16 && g < 16, $"near the blue corner: {r},{g},{b}");
        (r, g, b) = Framebuffer.ToRgb(rig.Pixel(200, 101));
        Assert.InRange(r, 110, 140);
        Assert.InRange(g, 110, 140);
        Assert.True(b < 16);
    }

    [Fact]
    public void ThreeEqualColoursDrawAFlatTriangle()
    {
        var rig = new Rig();
        rig.Put(0, (0, 0, 0, 0x1234), (200, 0, 0, 0x1234), (0, 200, 0, 0x1234));
        rig.Draw(0, 1);
        Assert.All(rig.Lit(), p => Assert.Equal(0x1234, rig.Pixel(p.Item1, p.Item2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNearerTriangleIsDrawnWhateverTheOrder(bool nearFirst)
    {
        var rig = new Rig(depth: true);
        var near = new[] { (50, 50, 1000, Red), (200, 50, 1000, Red), (50, 200, 1000, Red) };
        var far = new[] { (100, 100, 5000, Blue), (250, 100, 5000, Blue), (100, 250, 5000, Blue) };
        rig.Put(0, nearFirst ? near : far);
        rig.Put(12, nearFirst ? far : near);
        rig.Draw(0, 2);
        Assert.Equal(Red, rig.Pixel(110, 110));
        Assert.Equal(Blue, rig.Pixel(220, 110));
        Assert.Equal(Red, rig.Pixel(60, 60));
        Assert.Equal(1000, rig.Depth.DepthAt(110, 110));
        Assert.Equal(5000, rig.Depth.DepthAt(220, 110));
        Assert.Equal(DepthBuffer.Far, rig.Depth.DepthAt(400, 400));
        Assert.Equal(nearFirst, rig.Rasterizer.PixelsHidden > 0);
    }

    [Fact]
    public void DepthIsInterpolatedAcrossTheTriangle()
    {
        var rig = new Rig(depth: true);
        rig.Put(0, (0, 0, 0, White), (400, 0, 40000, White), (0, 400, 0, White));
        rig.Draw(0, 1);
        Assert.InRange(rig.Depth.DepthAt(200, 0), 19000, 21000);
        Assert.True(rig.Depth.DepthAt(10, 0) < rig.Depth.DepthAt(300, 0));
    }

    [Fact]
    public void EveryStageCostsItsBusTransfers()
    {
        var flat = new Rig();
        flat.Put(0, (10, 10, 100, White), (60, 20, 100, White), (25, 50, 100, White));
        int flatTicks = flat.Draw(0, 1);
        long pixels = flat.Rasterizer.PixelsDrawn;
        int rows = flat.Lit().Select(p => p.Item2).Distinct().Count();
        Assert.Equal(2 * Rasterizer.WordsPerTriangle + 2 * rows + pixels, flatTicks);

        var deep = new Rig(depth: true);
        deep.Put(0, (10, 10, 100, White), (60, 20, 100, White), (25, 50, 100, White));
        deep.Put(12, (10, 10, 200, Red), (60, 20, 200, Red), (25, 50, 200, Red));
        int drawn = deep.Draw(0, 1);
        Assert.Equal(2 * Rasterizer.WordsPerTriangle + 2 * rows + 3 * pixels, drawn);
        int hidden = deep.Draw(12, 1);
        Assert.Equal(2 * Rasterizer.WordsPerTriangle + 2 * rows + 2 * pixels, hidden);
        Assert.Equal(pixels, deep.Rasterizer.PixelsHidden);
        Assert.Equal(White, deep.Pixel(30, 25));
    }

    [Fact]
    public void ItReportsStatusAndRaisesAnInterruptWhenDone()
    {
        var rig = new Rig();
        rig.Put(0, (0, 0, 0, White), (20, 0, 0, White), (0, 20, 0, White));
        rig.Cpu.Data = 0; rig.Cpu.Enable("output"); rig.Rasterizer.Enable("loadaddr"); rig.Tick();
        rig.Cpu.Data = 1; rig.Cpu.Enable("output"); rig.Rasterizer.Enable("loadcount"); rig.Tick();
        rig.Rasterizer.Enable("start"); rig.Tick();
        Assert.True(rig.Rasterizer.Busy);
        rig.Rasterizer.Enable("status"); rig.Cpu.Enable("load"); rig.Tick();
        Assert.Equal(1, rig.Cpu.Data);
        Assert.False(rig.Rasterizer.TakeInterruptRequest());
        while (rig.Rasterizer.Busy) rig.Tick();
        Assert.True(rig.Rasterizer.TakeInterruptRequest());
        Assert.False(rig.Rasterizer.TakeInterruptRequest());
        Assert.Equal(1, rig.Rasterizer.JobsDone);
    }

    [Fact]
    public void TheDeviceOrderMakesNoDifference()
    {
        HashSet<(int, int)> Draw(bool reversed)
        {
            var rig = new Rig(depth: true, reversed: reversed);
            rig.Put(0, (10, 10, 50, Red), (90, 30, 50, Green), (40, 80, 50, Blue));
            rig.Draw(0, 1);
            return rig.Lit();
        }
        Assert.Equal(Draw(false).OrderBy(p => p), Draw(true).OrderBy(p => p));
    }

    [Fact]
    public void PixelsOffTheScreenAreNotDrawn()
    {
        var rig = new Rig();
        rig.Put(0, (-100, -50, 0, White), (700, 10, 0, White), (20, 600, 0, White));
        rig.Draw(0, 1);
        var lit = rig.Lit();
        Assert.Contains((0, 0), lit);
        Assert.All(lit, p => Assert.True(p.Item1 < Framebuffer.Width && p.Item2 < Framebuffer.Height));
        Assert.True(lit.Count > 10000);
    }

    [Fact]
    public void ADegenerateTriangleDrawsNothing()
    {
        var rig = new Rig();
        rig.Put(0, (10, 10, 0, White), (20, 20, 0, White), (30, 30, 0, White));
        int ticks = rig.Draw(0, 1);
        Assert.Empty(rig.Lit());
        Assert.Equal(2 * Rasterizer.WordsPerTriangle, ticks);
    }

    [Fact]
    public void BusTransfersNameWhoTookTheValue()
    {
        var rig = new Rig(depth: true);
        rig.Put(0, (0, 0, 0, White), (20, 0, 0, White), (0, 20, 0, White));
        rig.Cpu.Data = 1; rig.Cpu.Enable("output"); rig.Rasterizer.Enable("loadcount"); rig.Tick();
        rig.Rasterizer.Enable("start"); rig.Tick();
        rig.Tick();
        Assert.Contains(("list", "lmem"), rig.Rasterizer.LastReaders);
        rig.Tick();
        Assert.Contains(("list", "rast"), rig.Rasterizer.LastReaders);
        while (rig.Rasterizer.Stage == "reading") rig.Tick();
        rig.Tick();
        Assert.Contains(("video", "fb"), rig.Rasterizer.LastReaders);
        Assert.Contains(("video", "zb"), rig.Rasterizer.LastReaders);
    }

    private static Machine RunGpu(string program, long limit = 3_000_000, Func<Machine, bool> until = null)
    {
        var package = BuiltInPackages.Get("GPU-16");
        var c = new Machine(package.Machine, package.Programs.Single(p => p.Name == program).Source) { RecordHistory = false };
        long ticks = 0;
        while (!c.IsHalted && !(until?.Invoke(c) ?? false)) { c.SingleStep(); Assert.True(++ticks < limit, $"{program} ran too long"); }
        Assert.Empty(c.MicrocodeWarnings);
        return c;
    }
    private static int Pixel(Machine c, int x, int y) { return c.Device<Framebuffer>("fb").Pixels[y * Framebuffer.Width + x]; }

    [Fact]
    public void GpuFlatTriangleIsOneColour()
    {
        var c = RunGpu("One flat triangle");
        var rast = c.Device<Rasterizer>("rast");
        Assert.Equal(1, rast.TrianglesDrawn);
        Assert.Equal(0xFEE0, Pixel(c, 320, 300));
        Assert.Equal(0, Pixel(c, 20, 20));
        Assert.All(c.Device<Framebuffer>("fb").Pixels.Where(p => p != 0), p => Assert.Equal(0xFEE0, p));
    }

    [Fact]
    public void GpuShadedTriangleHasItsCornerColours()
    {
        var c = RunGpu("A shaded triangle");
        Assert.True(Framebuffer.ToRgb(Pixel(c, 145, 417)).R > 230);
        Assert.True(Framebuffer.ToRgb(Pixel(c, 320, 66)).G > 230);
        Assert.True(Framebuffer.ToRgb(Pixel(c, 495, 417)).B > 230);
        Assert.True(c.Device<Framebuffer>("fb").Pixels.Distinct().Count() > 1000);
    }

    [Fact]
    public void GpuTrianglesCutThroughEachOther()
    {
        var c = RunGpu("Two triangles through each other");
        var rast = c.Device<Rasterizer>("rast");
        Assert.Equal(2, rast.TrianglesDrawn);
        Assert.Equal(0xF800, Pixel(c, 250, 220));
        Assert.Equal(0x001F, Pixel(c, 390, 220));
        Assert.True(rast.PixelsHidden > 1000);
    }

    [Fact]
    public void GpuPinwheelIsDrawnWhileTheCpuPrints()
    {
        var c = RunGpu("A pinwheel from one list");
        Assert.Equal(16, c.Device<Rasterizer>("rast").TrianglesDrawn);
        var lcd = c.Device<CharacterDisplay>("lcd").Text;
        Assert.Contains("....", lcd);
        Assert.EndsWith("D", lcd.TrimEnd());
    }

    [Fact]
    public void GpuCubeTurnsFromFrameToFrame()
    {
        var frames = new List<ushort[]>();
        long lastJobsDone = 0;
        RunGpu("A spinning cube", 2_000_000, m =>
        {
            var rast = m.Device<Rasterizer>("rast");
            if (rast.JobsDone != lastJobsDone)
            {
                lastJobsDone = rast.JobsDone;
                frames.Add((ushort[])m.Device<Framebuffer>("fb").Pixels.Clone());
            }
            return frames.Count == 3;
        });
        Assert.All(frames, f => Assert.InRange(f.Count(p => p != 0), 20000, 80000));
        Assert.NotEqual(frames[0], frames[2]);
        var c = RunGpu("A spinning cube", 1_000_000, m => m.Device<Rasterizer>("rast").JobsDone == 1);
        Assert.True(c.Device<Rasterizer>("rast").PixelsHidden > 1000);
    }

    [Fact]
    public void SkipMovesTheCursorWithoutWriting()
    {
        var bus = new Bus();
        var screen = new Framebuffer("F", "fb", bus);
        bus.devices.Add(screen);
        screen.Enable("skip");
        bus.Clk();
        Assert.Equal((1, 0), (screen.X, screen.Y));
        Assert.Equal(0, screen.WriteCount);
    }

    [Fact]
    public void TheDepthBufferReadsWritesAndSteps()
    {
        var bus = new Bus();
        var depth = new DepthBuffer("Z", "zb", bus);
        var source = new Register("S", "s", bus);
        bus.devices.Add(source);
        bus.devices.Add(depth);
        Assert.Equal(DepthBuffer.Far, depth.DepthAt(5, 3));
        source.Data = 5; source.Enable("output"); depth.Enable("loadx"); bus.Clk();
        source.Data = 3; source.Enable("output"); depth.Enable("loady"); bus.Clk();
        source.Data = 1234; source.Enable("output"); depth.Enable("load"); depth.Enable("next"); bus.Clk();
        Assert.Equal(1234, depth.DepthAt(5, 3));
        Assert.Equal((6, 3), (depth.X, depth.Y));
        source.Data = 5; source.Enable("output"); depth.Enable("loadx"); bus.Clk();
        depth.Enable("output"); source.Enable("load"); bus.Clk();
        Assert.Equal(1234, source.Data);
        depth.Enable("clear"); bus.Clk();
        Assert.Equal(DepthBuffer.Far, depth.DepthAt(5, 3));
        Assert.Equal((0, 0), (depth.X, depth.Y));
    }

    private static (MultiplyAccumulate Mac, Register Io, Bus Bus) Mac(int shift = 8)
    {
        var bus = new Bus();
        var mac = new MultiplyAccumulate("MAC", "mac", bus, shift);
        var io = new Register("IO", "io", bus);
        bus.devices.Add(io);
        bus.devices.Add(mac);
        return (mac, io, bus);
    }
    private static void Load((MultiplyAccumulate Mac, Register Io, Bus Bus) m, string line, int value)
    {
        m.Io.Data = value & 0xFFFF; m.Io.Enable("output"); m.Mac.Enable(line); m.Bus.Clk();
    }
    private static int Output((MultiplyAccumulate Mac, Register Io, Bus Bus) m)
    {
        m.Mac.Enable("output"); m.Io.Enable("load"); m.Bus.Clk();
        return (short)m.Io.Data;
    }

    [Fact]
    public void MacMultipliesAndAccumulatesInFixedPoint()
    {
        const int FixedPointOne = 1 << 8;
        var m = Mac();
        Load(m, "loada", 3 * FixedPointOne / 2); Load(m, "loadb", 2 * FixedPointOne); m.Mac.Enable("mul"); m.Bus.Clk();
        Assert.Equal(3 * FixedPointOne, Output(m));
        Load(m, "loada", 100); Load(m, "loadb", FixedPointOne / 2); m.Mac.Enable("mul"); m.Bus.Clk();
        Load(m, "loada", 50); Load(m, "loadb", FixedPointOne / 4); m.Mac.Enable("mac"); m.Bus.Clk();
        Assert.Equal(62, Output(m));
        Load(m, "loada", -3); Load(m, "loadb", FixedPointOne); m.Mac.Enable("mul"); m.Bus.Clk();
        Assert.Equal(-3, Output(m));
    }

    [Fact]
    public void MacDividesForPerspective()
    {
        var m = Mac();
        Load(m, "loada", 60); Load(m, "loadb", 200); m.Mac.Enable("mul"); m.Bus.Clk();
        Load(m, "loadb", 300); m.Mac.Enable("div"); m.Bus.Clk();
        Assert.Equal(40, Output(m));
        Load(m, "loada", -60); Load(m, "loadb", 200); m.Mac.Enable("mul"); m.Bus.Clk();
        Load(m, "loadb", 300); m.Mac.Enable("div"); m.Bus.Clk();
        Assert.Equal(-40, Output(m));
        Load(m, "loadb", 0); m.Mac.Enable("div"); m.Bus.Clk();
        Assert.Equal(short.MinValue, Output(m));
    }

    [Fact]
    public void MacOutputIsClampedAndOneOperationRunsPerTick()
    {
        var m = Mac();
        Load(m, "loada", 30000); Load(m, "loadb", 30000); m.Mac.Enable("mul"); m.Bus.Clk();
        Assert.Equal(short.MaxValue, Output(m));
        var raw = Mac(shift: 0);
        Load(raw, "loada", 7); Load(raw, "loadb", -6); raw.Mac.Enable("mul"); raw.Bus.Clk();
        Assert.Equal(-42, Output(raw));
        m.Mac.Enable("mul");
        Assert.Throws<Exception>(() => m.Mac.Enable("mac"));
    }

    [Fact]
    public void CubeWithoutItsWaitShortsTheListBus()
    {
        var package = BuiltInPackages.Get("GPU-16");
        var source = package.Program("A spinning cube").Source;
        var unguarded = source.Replace("wait:      GST\n           CMPI   0\n           JNE    wait\n", "wait:      NOP\n");
        Assert.NotEqual(source, unguarded);
        var c = new Machine(package.Machine, unguarded) { RecordHistory = false };
        var e = Record.Exception(() => { for (int i = 0; i < 1_000_000; i++) c.SingleStep(); });
        Assert.NotNull(e);
        Assert.Contains("on bus 'list'", e.Message);
    }

    [Fact]
    public void FlipShowsTheTriangleOnlyAfterSwap()
    {
        var flip = new Machine(BuiltInPackages.Get("FLIP-16").Machine, BuiltInPackages.Get("FLIP-16").Program("A triangle drawn out of sight").Source) { RecordHistory = false };
        var fb = flip.Device<DoubleFramebuffer>("fb");
        while (!flip.IsHalted)
        {
            flip.SingleStep();
            if (fb.Swaps == 0) Assert.Equal(0, fb.Shown[240 * Framebuffer.Width + 320]);
        }
        Assert.Equal(1, fb.Swaps);
        var gpu = new Machine(BuiltInPackages.Get("GPU-16").Machine, BuiltInPackages.Get("GPU-16").Program("One flat triangle").Source) { RecordHistory = false };
        while (!gpu.IsHalted) gpu.SingleStep();
        Assert.Equal(gpu.Device<Framebuffer>("fb").Pixels, fb.Shown);
        Assert.Contains(fb.Shown, p => p != 0);
    }

    [Fact]
    public void FlipCubeChangesTheScreenOnlyAtSwaps()
    {
        var c = new Machine(BuiltInPackages.Get("FLIP-16").Machine, BuiltInPackages.Get("FLIP-16").Program("A spinning cube").Source) { RecordHistory = false };
        var fb = c.Device<DoubleFramebuffer>("fb");
        while (fb.Swaps < 2) c.SingleStep();
        var shown = (ushort[])fb.Shown.Clone();
        Assert.Contains(shown, p => p != 0);
        long checkedTicks = 0;
        while (fb.Swaps == 2)
        {
            c.SingleStep();
            if (++checkedTicks % 5000 == 0) Assert.Equal(shown, fb.Shown);
        }
        Assert.True(checkedTicks > 50_000, $"a frame took only {checkedTicks} ticks");
        Assert.NotEqual(shown, fb.Shown);
        Assert.Empty(c.MicrocodeWarnings);
    }

    private sealed class ManualTime : TimeProvider
    {
        public long Milliseconds;
        public override long GetTimestamp() => Milliseconds;
        public override long TimestampFrequency => 1000;
    }

    private static long[] SwapTimes(string program, int swaps)
    {
        var time = new ManualTime();
        var package = BuiltInPackages.Get("FLIP-16");
        var c = new Machine(package.Machine, package.Program(program).Source, DeviceRegistry.CreateDefault(time)) { RecordHistory = false };
        var fb = c.Device<DoubleFramebuffer>("fb");
        var times = new System.Collections.Generic.List<long>();
        long lastSwaps = 0;
        while (times.Count < swaps && time.Milliseconds < 60_000)
        {
            for (int i = 0; i < 2000; i++)
            {
                c.SingleStep();
                if (fb.Swaps != lastSwaps) { lastSwaps = fb.Swaps; times.Add(time.Milliseconds); }
            }
            time.Milliseconds++;
        }
        Assert.Empty(c.MicrocodeWarnings);
        return times.ToArray();
    }

    [Fact]
    public void TheSteadyCubeSwapsOnTheBeat()
    {
        var gaps = (long[] times) => times.Zip(times.Skip(1), (a, b) => b - a).Skip(1).ToArray();
        var plain = gaps(SwapTimes("A spinning cube", 20));
        Assert.True(plain.Max() - plain.Min() >= 5, $"the plain cube's frames took {string.Join(", ", plain)} ms");
        Assert.All(plain, gap => Assert.InRange(gap, 55, 85));
        var steady = gaps(SwapTimes("A steady spinning cube", 20));
        Assert.All(steady, gap => Assert.InRange(gap, 99, 101));
        Assert.Equal(100.0, steady.Average(), 1);
    }

    [Fact]
    public void TheWireframeCubeDrawsColouredEdges()
    {
        var package = BuiltInPackages.Get("FLIP-16");
        var c = new Machine(package.Machine, package.Program("A spinning wireframe cube").Source) { RecordHistory = false };
        var fb = c.Device<DoubleFramebuffer>("fb");
        while (fb.Swaps < 3) c.SingleStep();
        Assert.Equal(24, c.Device<Rasterizer>("rast").Count);
        var colours = fb.Shown.Where(p => p != 0).Distinct().ToList();
        int lit = fb.Shown.Count(p => p != 0);
        Assert.InRange(colours.Count, 8, 12);
        Assert.InRange(lit, 1000, 20_000);
        Assert.Empty(c.MicrocodeWarnings);
    }

    [Fact]
    public void FlipIsGpuWithADoubleBufferedScreenAndSwap()
    {
        var gpu = BuiltInPackages.Get("GPU-16").Machine;
        var flip = BuiltInPackages.Get("FLIP-16").Machine;
        Assert.Equal("framebuffer", gpu.FindDevice("fb").Type);
        Assert.Equal("doubleFramebuffer", flip.FindDevice("fb").Type);
        var gpuOps = gpu.Decoder.Microcode.Instructions.Select(i => i.Mnemonic).ToList();
        Assert.Equal(gpuOps.Concat(new[] { "SWAP", "TIMI", "TST", "TACK" }), flip.Decoder.Microcode.Instructions.Select(i => i.Mnemonic));
        var gpuRom = new DecoderRom(gpu.Decoder.Microcode);
        var flipRom = new DecoderRom(flip.Decoder.Microcode);
        Assert.All(gpuOps, op => Assert.Equal(gpuRom.FetchByteCodeFromMnemonic(op), flipRom.FetchByteCodeFromMnemonic(op)));
    }
}
