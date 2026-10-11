using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class BlitterTests
{
    private readonly Bus host = new Bus("host");
    private readonly Bus video = new Bus("video");
    private readonly Register cpu;
    private readonly Framebuffer screen;
    private readonly Blitter blit;

    public BlitterTests()
    {
        cpu = new Register("CPU", "cpu", host);
        screen = new Framebuffer("FB", "fb", video);
        blit = new Blitter("BLIT", "blit", host, video, screen);
    }

    private void Tick() => Clocking.Tick(new[] { host, video }, new IBusDevice[] { cpu, blit, screen });
    private void LoadIntoBlitter(string line, int value)
    {
        cpu.Data = value;
        cpu.Enable("output");
        blit.Enable(line);
        Tick();
    }
    private void Job(int x, int y, int w, int h, int colour)
    {
        LoadIntoBlitter("loadx", x);
        LoadIntoBlitter("loady", y);
        LoadIntoBlitter("loadw", w);
        LoadIntoBlitter("loadh", h);
        LoadIntoBlitter("loadcolour", colour);
        blit.Enable("start");
        Tick();
    }
    private int Status()
    {
        blit.Enable("status");
        cpu.Enable("load");
        Tick();
        return cpu.Data;
    }

    [Fact]
    public void LinesMatchTheRegistry()
    {
        var info = DeviceRegistry.CreateDefault().Info("blitter");
        Assert.Equal(blit.SignalLines(), info.ControlLines.Select(l => l.Name));
        Assert.Equal(new[] { "host", "video" }, info.Ports);
        Assert.Equal("screen", Assert.Single(info.Connections).Name);
    }

    [Fact]
    public void FillsTheRectangleByItselfAndThenGoesIdle()
    {
        Job(10, 20, 3, 2, 0xF800);
        Assert.True(blit.Busy);
        int ticks = 0;
        while (blit.Busy) { Tick(); ticks++; }
        Assert.Equal(2 * (2 + 3), ticks);
        for (int y = 20; y < 22; y++)
            for (int x = 10; x < 13; x++)
                Assert.Equal(0xF800, screen.Pixels[y * Framebuffer.Width + x]);
        Assert.Equal(0, screen.Pixels[20 * Framebuffer.Width + 13]);
        Assert.Equal(0, screen.Pixels[22 * Framebuffer.Width + 10]);
        Assert.Equal(6, blit.PixelsDrawn);
        Assert.Equal(1, blit.JobsDone);
    }

    [Fact]
    public void StatusReadsBusyThenIdle()
    {
        Assert.Equal(0, Status());
        Job(0, 0, 4, 4, 0x07E0);
        Assert.Equal(1, Status());
        for (int i = 0; i < 40; i++) Tick();
        Assert.Equal(0, Status());
    }

    [Fact]
    public void TheHostBusStaysFreeWhileItPaints()
    {
        Job(0, 0, 8, 8, 0x001F);
        var other = new Register("OTHER", "other", host);
        for (int i = 0; i < 10; i++)
        {
            cpu.Data = i;
            cpu.Enable("output");
            other.Enable("load");
            Clocking.Tick(new[] { host, video }, new IBusDevice[] { cpu, other, blit, screen });
            Assert.Equal(i, other.Data);
            Assert.Same(blit, video.Writer);
        }
    }

    [Fact]
    public void AnEmptyRectangleDoesNotStart()
    {
        Job(0, 0, 0, 5, 0xFFFF);
        Assert.False(blit.Busy);
        Assert.Throws<ArgumentException>(() => new Blitter("B", "b", host, video, null));
    }
}
