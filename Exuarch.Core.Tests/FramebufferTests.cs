using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class FramebufferTests
{
    private readonly Bus bus = new Bus();
    private readonly Register source;
    private readonly Framebuffer screen;

    public FramebufferTests()
    {
        source = new Register("SRC", "src", bus);
        screen = new Framebuffer("SCREEN", "fb", bus);
        bus.devices.Add(source);
        bus.devices.Add(screen);
    }

    private void Put(string line, int value)
    {
        source.Data = value;
        source.Enable("output");
        screen.Enable(line);
        bus.Clk();
    }

    [Fact]
    public void PlotWritesAtTheCursorAndMovesRight()
    {
        Put("loadx", 10);
        Put("loady", 20);
        Put("plot", 0xF800);
        Put("plot", 0x07E0);
        Assert.Equal(0xF800, screen.Pixels[20 * 640 + 10]);
        Assert.Equal(0x07E0, screen.Pixels[20 * 640 + 11]);
        Assert.Equal((12, 20), (screen.X, screen.Y));
    }

    [Fact]
    public void CursorWrapsToTheNextRowAndBackToTheTop()
    {
        Put("loadx", 639);
        Put("loady", 479);
        Put("plot", 1);
        Assert.Equal((0, 0), (screen.X, screen.Y));
        Put("loadx", 639);
        Put("loady", 5);
        Put("plot", 2);
        Assert.Equal((0, 6), (screen.X, screen.Y));
        Put("loadx", 700);
        Assert.Equal(60, screen.X);
    }

    [Fact]
    public void ClearBlanksTheScreenAndHomesTheCursor()
    {
        Put("loadx", 5);
        Put("plot", 0xFFFF);
        screen.TakeDirtyRegion();
        screen.Enable("clear");
        bus.Clk();
        Assert.All(screen.Pixels, p => Assert.Equal(0, p));
        Assert.Equal((0, 0), (screen.X, screen.Y));
        Assert.Equal((0, 0, 640, 480), screen.TakeDirtyRegion());
    }

    [Fact]
    public void DirtyRegionCoversOnlyWhatChanged()
    {
        Assert.Null(screen.TakeDirtyRegion());
        Put("loadx", 100);
        Put("loady", 50);
        Put("plot", 1);
        Put("plot", 1);
        Put("loady", 60);
        Put("plot", 1);
        Assert.Equal((100, 50, 3, 11), screen.TakeDirtyRegion());
        Assert.Null(screen.TakeDirtyRegion());
    }

    [Theory]
    [InlineData(0xF800, 255, 0, 0)]
    [InlineData(0x07E0, 0, 255, 0)]
    [InlineData(0x001F, 0, 0, 255)]
    [InlineData(0xFFFF, 255, 255, 255)]
    [InlineData(0x0000, 0, 0, 0)]
    public void Rgb565Converts(int rgb565, int r, int g, int b)
    {
        Assert.Equal(((byte)r, (byte)g, (byte)b), Framebuffer.ToRgb(rgb565));
        Assert.Equal(rgb565, Framebuffer.FromRgb(r, g, b));
    }

    [Fact]
    public void ToRgbaGivesCanvasBytes()
    {
        Put("plot", 0xF800);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 0, 255 }, screen.ToRgba(0, 0, 2, 1));
    }

    [Fact]
    public void Rgb565BytesAreTheRawWordsOfARegion()
    {
        screen.Pixels[1 * Framebuffer.Width + 2] = 0xF81F;
        screen.Pixels[1 * Framebuffer.Width + 3] = 0x07E0;
        screen.Pixels[2 * Framebuffer.Width + 2] = 0x1234;
        Assert.Equal(new byte[] { 0x1F, 0xF8, 0xE0, 0x07, 0x34, 0x12, 0x00, 0x00 }, screen.ToRgb565Bytes(2, 1, 2, 2));
        Assert.Equal(Framebuffer.Width * Framebuffer.Height * 2, screen.ToRgb565Bytes(0, 0, Framebuffer.Width, Framebuffer.Height).Length);
    }

    [Fact]
    public void ControlLinesMatchTheRegistry()
    {
        Assert.Equal(screen.SignalLines(), DeviceRegistry.CreateDefault().Info("framebuffer").ControlLines.Select(l => l.Name));
    }

    [Fact]
    public void BandsProgramDrawsTheBandsAndHalts()
    {
        var c = new Machine(MachineDefinition.FromJson(ExampleData.MACHINE), ExampleData.BANDS) { RecordHistory = false };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < 2_000_000, "did not halt"); }
        var fb = c.Device<Framebuffer>("fb");
        Assert.Equal(0xF800, fb.Pixels[224 * 640 + 0]);
        Assert.Equal(0xF800, fb.Pixels[227 * 640 + 639]);
        Assert.Equal(0xFD20, fb.Pixels[228 * 640 + 0]);
        Assert.Equal(0xFFFF, fb.Pixels[255 * 640 + 639]);
        Assert.Equal(0, fb.Pixels[223 * 640 + 639]);
        Assert.Equal(0, fb.Pixels[256 * 640]);
    }

    [Fact]
    public void GradientProgramDrawsASmoothGradientAndHalts()
    {
        var c = new Machine(MachineDefinition.FromJson(ExampleData.MACHINE), ExampleData.GRADIENT) { RecordHistory = false };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < 3_000_000, "did not halt"); }
        var fb = c.Device<Framebuffer>("fb");
        Assert.Equal(Framebuffer.Width * Framebuffer.Height, fb.WriteCount);
        for (int y = 0; y < Framebuffer.Height; y += 7)
        {
            for (int x = 0; x < Framebuffer.Width; x += 13)
            {
                int expected = (x / 20) << 11 | (y / 8) << 5 | 0x10;
                Assert.True(expected == fb.Pixels[y * Framebuffer.Width + x], $"pixel {x},{y}: expected 0x{expected:X4}, found 0x{fb.Pixels[y * Framebuffer.Width + x]:X4}");
            }
        }
        Assert.Empty(c.MicrocodeWarnings);
    }
}

public class DoubleFramebufferTests
{
    private readonly Bus bus = new Bus();
    private readonly Register source;
    private readonly DoubleFramebuffer screen;

    public DoubleFramebufferTests()
    {
        source = new Register("SRC", "src", bus);
        screen = new DoubleFramebuffer("SCREEN", "fb", bus);
        bus.devices.Add(source);
        bus.devices.Add(screen);
    }

    private void Put(params (string Line, int Value)[] lines)
    {
        source.Data = lines[0].Value;
        source.Enable("output");
        foreach (var (line, _) in lines) screen.Enable(line);
        bus.Clk();
    }
    private void Put(string line, int value) { Put((line, value)); }
    private int ShownAt(int x, int y) { return screen.Shown[y * 640 + x]; }

    [Fact]
    public void DrawingStaysOutOfSightUntilSwap()
    {
        screen.TakeDirtyRegion();
        Put("plot", 0xF800);
        Assert.Equal(0xF800, screen.Pixels[0]);
        Assert.Equal(0, ShownAt(0, 0));
        Assert.Null(screen.TakeDirtyRegion());
        Assert.Equal(0, screen.FrontBuffer);
        Put("swap", 0);
        Assert.Equal(0xF800, ShownAt(0, 0));
        Assert.Equal(0, screen.Pixels[0]);
        Assert.Equal(1, screen.FrontBuffer);
        Assert.Equal((0, 0, 640, 480), screen.TakeDirtyRegion());
        Assert.Equal((1, 0), (screen.X, screen.Y));
        Assert.Equal(0xF800, screen.LastWriteValue);
    }

    [Fact]
    public void SwapsAlternateTheTwoBuffers()
    {
        Put("plot", 1);
        Put("swap", 0);
        Put("loadx", 0);
        Put("plot", 2);
        Put("swap", 0);
        Assert.Equal(2, ShownAt(0, 0));
        Assert.Equal(1, screen.Pixels[0]);
        Assert.Equal(0, screen.FrontBuffer);
        Assert.Equal(2, screen.Swaps);
    }

    [Fact]
    public void APlotInTheSwapTickIsPartOfTheFrameShown()
    {
        Put(("plot", 0x001F), ("swap", 0x001F));
        Assert.Equal(0x001F, ShownAt(0, 0));
    }

    [Fact]
    public void ClearBlanksOnlyTheBackBuffer()
    {
        Put("plot", 7);
        Put("swap", 0);
        Put("plot", 9);
        Put("clear", 0);
        Assert.Equal(7, ShownAt(0, 0));
        Assert.All(screen.Pixels, p => Assert.Equal(0, p));
        Assert.Equal((0, 0), (screen.X, screen.Y));
    }

    [Fact]
    public void TheRasterizerCanDrawOnIt()
    {
        var gpu = BuiltInPackages.Get("GPU-16").Machine;
        gpu.Devices.First(d => d.Id == "fb").Type = "doubleFramebuffer";
        var c = new Machine(gpu, BuiltInPackages.Get("GPU-16").Program("One flat triangle").Source) { RecordHistory = false };
        for (int i = 0; i < 2_000_000 && !c.IsHalted; i++) c.SingleStep();
        var fb = c.Device<DoubleFramebuffer>("fb");
        Assert.True(fb.WriteCount > 0);
        Assert.Contains(fb.Pixels, p => p != 0);
        Assert.All(fb.Shown, p => Assert.Equal(0, p));
        Assert.Contains("swap", fb.SignalLines());
    }
}
