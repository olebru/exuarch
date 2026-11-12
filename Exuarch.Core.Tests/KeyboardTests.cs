using System.Linq;
using System.Text.Json;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class KeyboardTests
{
    private const int A = 0x1E;
    private const int S = 0x1F;
    private readonly Bus bus = new Bus();
    private readonly Register target;
    private readonly Keyboard keyboard;

    public KeyboardTests()
    {
        target = new Register("T", "t", bus);
        keyboard = new Keyboard("KEYBOARD", "kbd", bus);
        bus.devices.Add(target);
        bus.devices.Add(keyboard);
    }

    private int Read()
    {
        keyboard.Enable("output");
        target.Enable("load");
        bus.Clk();
        return target.Data;
    }

    [Fact]
    public void HasOnlyAnOutputLine()
    {
        Assert.Equal(new[] { "output" }, keyboard.SignalLines());
        var info = DeviceRegistry.CreateDefault().Info("keyboard");
        Assert.Equal(new[] { "output" }, info.ControlLines.Select(l => l.Name));
        Assert.Equal("I/O", info.Category);
    }

    [Fact]
    public void AnEmptyQueueReadsZero()
    {
        Assert.Equal(Keyboard.Empty, Read());
        Assert.Equal(Keyboard.Empty, Read());
    }

    [Fact]
    public void EachReadTakesTheOldestCodeAndAReleaseAddsTheReleaseBit()
    {
        keyboard.Press(A);
        keyboard.Press(S);
        keyboard.Release(A);
        keyboard.Release(S);
        Assert.Equal(new[] { A, S, A | 0x80, S | 0x80, 0 }, Enumerable.Range(0, 5).Select(_ => Read()));
    }

    [Fact]
    public void AHeldKeyRepeatsItsPressCodeAndIsReleasedOnce()
    {
        keyboard.Press(A);
        keyboard.Press(A);
        keyboard.Release(A);
        keyboard.Release(A);
        Assert.Equal(new[] { A, A, A | 0x80 }, keyboard.Events);
    }

    [Fact]
    public void AFullQueueDropsTheNewCodes()
    {
        for (int i = 0; i < Keyboard.Capacity + 3; i++) keyboard.Press(0x10 + i % 10);
        Assert.Equal(Keyboard.Capacity, keyboard.Count);
        Assert.Equal(3, keyboard.Dropped);
        Assert.Equal(0x10, Read());
    }

    [Fact]
    public void EveryCodeAddedRaisesAnInterruptRequest()
    {
        Assert.False(keyboard.TakeInterruptRequest());
        keyboard.Press(A);
        Assert.True(keyboard.TakeInterruptRequest());
        Assert.False(keyboard.TakeInterruptRequest());
        keyboard.Press(A);
        Assert.True(keyboard.TakeInterruptRequest());
        keyboard.Release(A);
        Assert.True(keyboard.TakeInterruptRequest());
        keyboard.Release(A);
        Assert.False(keyboard.TakeInterruptRequest());
    }

    [Fact]
    public void ReleaseAllSendsAReleaseForEveryHeldKey()
    {
        keyboard.Press(XtScanCodes.LeftShift);
        keyboard.Press(A);
        keyboard.ReleaseAll();
        Assert.Empty(keyboard.Held);
        Assert.Equal(new[] { XtScanCodes.LeftShift, A, A | 0x80, XtScanCodes.LeftShift | 0x80 }, keyboard.Events);
    }

    [Fact]
    public void OnlyDrivesTheBusWhenOutputIsEnabledAndKeepsTheCodeUntilThen()
    {
        keyboard.Press(A);
        target.Data = 99;
        target.Enable("output");
        bus.Clk();
        Assert.Equal(99, bus.Data);
        Assert.Equal(1, keyboard.Count);
        Assert.Equal(A, Read());
    }

    [Fact]
    public void AMachineReadsScanCodesWithItsInputInstruction()
    {
        var json = JsonSerializer.Serialize(BuiltInPackages.Get("RISC-16").Machine, MachineDefinitionJsonContext.Default.MachineDefinition);
        var definition = JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.MachineDefinition);
        definition.Devices.Single(d => d.Id == "keys").Type = "keyboard";
        var risc = new Machine(definition, "IN R0\nIN R1\nIN R2\nHLT") { RecordHistory = false };
        risc.Device<Keyboard>("keys").Press(A);
        risc.Device<Keyboard>("keys").Release(A);
        while (!risc.IsHalted) risc.SingleStep();
        var registers = risc.Device<RegisterFile>("rf");
        Assert.Equal((A, A | 0x80, 0), (registers[0], registers[1], registers[2]));
    }

    [Fact]
    public void BrowserKeyCodesMapToXtScanCodes()
    {
        Assert.Equal(0x01, XtScanCodes.For("Escape"));
        Assert.Equal(0x02, XtScanCodes.For("Digit1"));
        Assert.Equal(0x0B, XtScanCodes.For("Digit0"));
        Assert.Equal(0x10, XtScanCodes.For("KeyQ"));
        Assert.Equal(A, XtScanCodes.For("KeyA"));
        Assert.Equal(0x2C, XtScanCodes.For("KeyZ"));
        Assert.Equal(0x1C, XtScanCodes.For("Enter"));
        Assert.Equal(0x39, XtScanCodes.For("Space"));
        Assert.Equal(0x44, XtScanCodes.For("F10"));
        Assert.Equal(0x48, XtScanCodes.For("ArrowUp"));
        Assert.Equal(0x48, XtScanCodes.For("Numpad8"));
        Assert.Equal(0x38, XtScanCodes.For("AltRight"));
        Assert.Equal(0x56, XtScanCodes.For("IntlBackslash"));
        Assert.Null(XtScanCodes.For("MetaLeft"));
        Assert.Null(XtScanCodes.For("F11"));
        Assert.All(XtScanCodes.All, entry => Assert.InRange(entry.Value, 1, 0x7F));
    }

    [Fact]
    public void TheLayoutsTranslateScanCodesWithAndWithoutShift()
    {
        var us = KeyboardLayouts.Values(KeyboardLayoutKind.US);
        var norwegian = KeyboardLayouts.Values(KeyboardLayoutKind.Norwegian);
        Assert.Equal(256, us.Length);
        Assert.Equal(('a', 'A'), ((char)us[A], (char)us[128 + A]));
        Assert.Equal(('2', '@'), ((char)us[0x03], (char)us[128 + 0x03]));
        Assert.Equal(('2', '"'), ((char)norwegian[0x03], (char)norwegian[128 + 0x03]));
        Assert.Equal(('ø', 'Ø'), ((char)norwegian[0x27], (char)norwegian[128 + 0x27]));
        Assert.Equal(('æ', 'Æ'), ((char)norwegian[0x28], (char)norwegian[128 + 0x28]));
        Assert.Equal(('å', 'Å'), ((char)norwegian[0x1A], (char)norwegian[128 + 0x1A]));
        Assert.Equal(('<', '>'), ((char)norwegian[0x56], (char)norwegian[128 + 0x56]));
        Assert.Equal((13, 8, ' '), (us[0x1C], us[0x0E], (char)us[0x39]));
        Assert.Equal((0, 0, 0), (us[XtScanCodes.LeftShift], us[XtScanCodes.Control], us[XtScanCodes.Up]));
    }

    [Theory]
    [InlineData(KeyboardLayoutKind.US)]
    [InlineData(KeyboardLayoutKind.Norwegian)]
    public void ALayoutAssemblesIntoTheSameCellsInARom(KeyboardLayoutKind kind)
    {
        var result = RomContents.Analyze(string.Join("\n", KeyboardLayouts.Lines(kind)), 256);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(KeyboardLayouts.Values(kind), result.Cells);
    }
}
