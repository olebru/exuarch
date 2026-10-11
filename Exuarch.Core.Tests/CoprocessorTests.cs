using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class CoprocessorTests
{
    private static MachinePackage Package() => BuiltInPackages.Get("COPRO-16");

    private static Machine Run(string source, bool record = false, int limit = 5_000_000)
    {
        var c = new Machine(Package().Machine, source) { RecordHistory = record };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < limit, "program did not halt"); }
        Assert.Empty(c.MicrocodeWarnings);
        return c;
    }
    private static ushort Pixel(Machine c, int x, int y) => c.Device<Framebuffer>("fb").Pixels[y * Framebuffer.Width + x];

    [Fact]
    public void TheBlitterBridgesTheMainAndVideoBuses()
    {
        var machine = Package().Machine;
        Assert.Equal(new[] { "main", "video" }, machine.Buses.Select(b => b.Id));
        var blit = machine.FindDevice("blit");
        Assert.Equal(("main", "video"), (blit.Buses["host"], blit.Buses["video"]));
        Assert.Equal("fb", blit.Connections["screen"]);
        Assert.Equal("video", machine.FindDevice("fb").Bus);
        Assert.Empty(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine));
    }

    [Fact]
    public void RectanglesArePaintedAndNamed()
    {
        var c = Run(Package().Program("Rectangles while the CPU writes").Source);
        Assert.Equal(0x2945, Pixel(c, 45, 45));
        Assert.Equal(0xF800, Pixel(c, 100, 100));
        Assert.Equal(0x07E0, Pixel(c, 500, 100));
        Assert.Equal(0x001F, Pixel(c, 100, 390));
        Assert.Equal(0xFFE0, Pixel(c, 500, 390));
        Assert.Equal(0xFFFF, Pixel(c, 320, 240));
        Assert.Equal(0, Pixel(c, 20, 20));
        var lcd = c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "");
        foreach (var name in new[] { "frame", "red", "green", "blue", "yellow", "white", "done" }) Assert.Contains(name, lcd);
        Assert.Contains(".", lcd);
    }

    [Fact]
    public void CheckerboardAlternates()
    {
        var c = Run(Package().Program("Checkerboard").Source);
        for (int row = 0; row < 6; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                var expected = (row + column) % 2 == 0 ? 0xFFFF : 0x0000;
                Assert.Equal(expected, Pixel(c, column * 80 + 40, row * 80 + 40));
            }
        }
        Assert.Equal(48, c.Device<Blitter>("blit").JobsDone);
        Assert.Contains(new string('#', 16), c.Device<CharacterDisplay>("lcd").Text.Replace("\n", ""));
    }

    [Fact]
    public void TheCpuKeepsWorkingWhileTheBlitterPaints()
    {
        var c = new Machine(Package().Machine, Package().Program("Rectangles while the CPU writes").Source) { RecordHistory = true };
        int both = 0;
        for (int i = 0; i < 20_000 && !c.IsHalted; i++)
        {
            c.SingleStep();
            var tick = c.LastTick;
            var main = tick.Transfers.Single(t => t.Bus == "main");
            var video = tick.Transfers.Single(t => t.Bus == "video");
            if (video.Driver == "blit" && main.Driver != null && main.Driver != "blit")
            {
                both++;
                Assert.Contains("fb", video.Readers);
            }
        }
        Assert.True(both > 1000, $"only {both} ticks used both buses");
    }
}
