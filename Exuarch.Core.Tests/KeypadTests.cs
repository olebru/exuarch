using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class KeypadTests
{
    private readonly Bus bus = new Bus();
    private readonly Register target;
    private readonly Keypad keys;

    public KeypadTests()
    {
        target = new Register("T", "t", bus);
        keys = new Keypad("KEYS", "keys", bus);
        bus.devices.Add(target);
        bus.devices.Add(keys);
    }

    private int Read()
    {
        keys.Enable("output");
        target.Enable("load");
        bus.Clk();
        return target.Data;
    }

    [Fact]
    public void HasOnlyAnOutputLineLikeARegister()
    {
        Assert.Equal(new[] { "output" }, keys.SignalLines());
        var info = DeviceRegistry.CreateDefault().Info("keypad");
        Assert.Equal(new[] { "output" }, info.ControlLines.Select(l => l.Name));
        Assert.Equal("I/O", info.Category);
    }

    [Fact]
    public void EachKeyHasItsOwnBit()
    {
        Assert.Equal(0, Read());
        keys.Press(Keypad.Keys.Up);
        Assert.Equal(1, Read());
        keys.Press(Keypad.Keys.Right);
        keys.Press(Keypad.Keys.Space);
        Assert.Equal(1 | 8 | 16, Read());
        keys.Release(Keypad.Keys.Up);
        keys.Press(Keypad.Keys.Down);
        keys.Press(Keypad.Keys.Left);
        Assert.Equal(2 | 4 | 8 | 16, Read());
    }

    [Fact]
    public void AHeldKeyReadsDownEveryTime()
    {
        keys.Press(Keypad.Keys.Left);
        Assert.Equal(4, Read());
        Assert.Equal(4, Read());
        keys.Release(Keypad.Keys.Left);
        Assert.Equal(0, Read());
    }

    [Fact]
    public void ATapBetweenReadsIsReadOnce()
    {
        keys.Press(Keypad.Keys.Space);
        keys.Release(Keypad.Keys.Space);
        Assert.Equal(16, keys.Data);
        Assert.Equal(16, Read());
        Assert.Equal(0, Read());
    }

    [Fact]
    public void OnlyDrivesTheBusWhenOutputIsEnabled()
    {
        keys.Press(Keypad.Keys.Up);
        target.Data = 99;
        target.Enable("output");
        bus.Clk();
        Assert.Equal(99, bus.Data);
        Assert.Equal(1, keys.Data);
    }

    [Fact]
    public void BothPackagesCanReadTheKeypad()
    {
        var byoc = new Machine(BuiltInPackages.Get("BYOC-16").Machine, "\tLKA\n\tHLT") { RecordHistory = false };
        byoc.Device<Keypad>("keys").Press(Keypad.Keys.Right);
        while (!byoc.IsHalted) byoc.SingleStep();
        Assert.Equal(8, byoc.Device<Register>("rega").Data);

        var risc = new Machine(BuiltInPackages.Get("RISC-16").Machine, "IN R0\nHLT") { RecordHistory = false };
        risc.Device<Keypad>("keys").Press(Keypad.Keys.Down);
        risc.Device<Keypad>("keys").Press(Keypad.Keys.Space);
        while (!risc.IsHalted) risc.SingleStep();
        Assert.Equal(2 | 16, risc.Device<RegisterFile>("rf")[0]);
    }

    [Fact]
    public void TheSketchMovesThePenWithTheKeys()
    {
        var c = new Machine(BuiltInPackages.Get("RISC-16").Machine, BuiltInPackages.Get("RISC-16").Program("Sketch with the arrow keys").Source) { RecordHistory = false };
        var keypad = c.Device<Keypad>("keys");
        for (int i = 0; i < 20000; i++) c.SingleStep();
        Assert.Equal((320, 240), (c.Device<RegisterFile>("rf")[1], c.Device<RegisterFile>("rf")[2]));

        keypad.Press(Keypad.Keys.Right);
        keypad.Press(Keypad.Keys.Up);
        for (int i = 0; i < 100000; i++) c.SingleStep();
        keypad.ReleaseAll();
        var (x, y) = (c.Device<RegisterFile>("rf")[1], c.Device<RegisterFile>("rf")[2]);
        Assert.True(x > 320 && y < 240, $"pen at {x}, {y}");
        Assert.Equal(x - 320, 240 - y);
        Assert.Equal(0xFFE0, c.Device<Framebuffer>("fb").Pixels[(y + 1) * Framebuffer.Width + x - 1]);
        Assert.Empty(c.MicrocodeWarnings);
    }
}
