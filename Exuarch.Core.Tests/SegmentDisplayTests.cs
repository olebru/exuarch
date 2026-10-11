using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class SegmentDisplayTests
{
    private readonly Bus bus = new Bus();
    private readonly Register source;
    private readonly SegmentDisplay display;

    public SegmentDisplayTests()
    {
        source = new Register("SRC", "src", bus);
        display = new SegmentDisplay("OUTPUT", "out", bus);
        bus.devices.Add(source);
        bus.devices.Add(display);
    }

    private void Show(int value, string line)
    {
        source.Data = value;
        source.Enable("output");
        display.Enable(line);
        bus.Clk();
    }

    [Fact]
    public void HasLoadLoadhexAndLoadsigned()
    {
        Assert.Equal(new[] { "load", "loadhex", "loadsigned" }, display.SignalLines());
        Assert.Equal(new[] { "load", "loadhex", "loadsigned" }, DeviceRegistry.CreateDefault().Info("segmentDisplay").ControlLines.Select(l => l.Name));
    }

    [Fact]
    public void ShowsZeroInDecimalBeforeAnyLoad()
    {
        Assert.Equal("     0", display.Text);
    }

    [Fact]
    public void LoadShowsTheBusInDecimalWithBlankLeadingDigits()
    {
        Show(42, "load");
        Assert.Equal("    42", display.Text);
        Assert.Equal(SegmentFormat.Decimal, display.Format);

        Show(65535, "load");
        Assert.Equal(" 65535", display.Text);
    }

    [Fact]
    public void LoadhexShowsTheBusAsFourHexDigits()
    {
        Show(42, "loadhex");
        Assert.Equal("  002A", display.Text);
        Assert.Equal(SegmentFormat.Hex, display.Format);

        Show(0xBEEF, "loadhex");
        Assert.Equal("  BEEF", display.Text);
    }

    [Fact]
    public void LoadsignedShowsTheBusAsATwosComplementNumber()
    {
        Show(42, "loadsigned");
        Assert.Equal("    42", display.Text);
        Assert.Equal(SegmentFormat.Signed, display.Format);

        Show(0xFFFF, "loadsigned");
        Assert.Equal("    -1", display.Text);

        Show(0x8000, "loadsigned");
        Assert.Equal("-32768", display.Text);

        Show(0x7FFF, "loadsigned");
        Assert.Equal(" 32767", display.Text);
    }

    [Fact]
    public void TheValueIsShownOnlyFromTheTickItIsLoaded()
    {
        Show(7, "load");
        source.Data = 9;
        source.Enable("output");
        bus.Clk();
        Assert.Equal("     7", display.Text);
    }
}
