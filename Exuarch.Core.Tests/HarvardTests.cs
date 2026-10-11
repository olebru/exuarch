using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class HarvardTests
{
    private static MachinePackage Package() => BuiltInPackages.Get("HARVARD-16");

    private static Machine Run(string source, int limit = 1_000_000, bool record = false)
    {
        var c = new Machine(Package().Machine, source) { RecordHistory = record };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < limit, "program did not halt"); }
        Assert.Empty(c.MicrocodeWarnings);
        return c;
    }
    private static int Reg(Machine c, string id) => c.Device<Register>(id).Data;

    [Fact]
    public void HasThreeBusesJoinedByTwoBridges()
    {
        var machine = Package().Machine;
        Assert.Equal(new[] { "ibus", "dbus", "iobus" }, machine.Buses.Select(b => b.Id));
        var bridges = machine.Devices.Where(d => d.Type == "dualPortRegister").ToDictionary(d => d.Id, d => (d.Buses["a"], d.Buses["b"]));
        Assert.Equal(("ibus", "dbus"), bridges["opr"]);
        Assert.Equal(("dbus", "iobus"), bridges["io"]);
        Assert.Equal("ibus", machine.FindDevice("pmem").Bus);
        Assert.Equal("dbus", machine.FindDevice("dmem").Bus);
        Assert.All(new[] { "lcd", "fb", "keys" }, id => Assert.Equal("iobus", machine.FindDevice(id).Bus));
        Assert.Empty(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine));
    }

    [Fact]
    public void HelloPrintsAStringReadFromProgramMemory()
    {
        var c = Run(Package().Program("Hello, world across three buses").Source);
        Assert.Equal("Hello from three buses!", c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd());
    }

    [Fact]
    public void FibonacciPrintsTheSequenceInDecimal()
    {
        var c = Run(Package().Program("Fibonacci on the LCD").Source);
        var text = c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "");
        Assert.Equal("1 1 2 3 5 8 13 21 34 55 89 144 233 377 610 987 1597 2584 4181 6765 10946 17711 28657 46368", text.Trim());
    }

    [Fact]
    public void GradientFillsTheScreen()
    {
        var c = Run(Package().Program("Colour gradient on the screen").Source, 10_000_000);
        var fb = c.Device<Framebuffer>("fb");
        Assert.Equal(Framebuffer.Width * Framebuffer.Height, fb.WriteCount);
        for (int y = 0; y < Framebuffer.Height; y += 7)
        {
            for (int x = 0; x < Framebuffer.Width; x += 13)
            {
                Assert.Equal((x / 20) << 11 | (y / 8) << 5 | 0x10, fb.Pixels[y * Framebuffer.Width + x]);
            }
        }
    }

    [Fact]
    public void AnAluOperationTakesTwoTicksFetchIncluded()
    {
        var c = new Machine(Package().Machine, "\tADD\n\tADD\n\tHLT") { RecordHistory = true };
        while (!c.IsHalted) c.SingleStep();
        Assert.Equal(2 + 2 + 2 + 1, c.Cycles);
    }

    [Fact]
    public void TwoBusesCarryTransfersInTheSameTick()
    {
        var c = new Machine(Package().Machine, "\tLDI\t5\n\tOUT\n\tHLT") { RecordHistory = true };
        while (!c.IsHalted) c.SingleStep();
        var busy = c.History.Select(t => t.Transfers.Where(x => x.Driver != null).Select(x => x.Bus).ToList()).ToList();
        Assert.Contains(busy, buses => buses.Contains("dbus") && buses.Contains("ibus"));
        Assert.Contains(busy, buses => buses.Contains("iobus") && buses.Contains("ibus"));
    }

    [Fact]
    public void LoadsStoresAndTheStackUseDataMemory()
    {
        var c = Run(@"
            LDI  7
            STA  100
            LBI  100
            LDX
            INA
            STX
            PUSH
            LDI  0
            POP
            LDB  100
            HLT");
        Assert.Equal(8, Reg(c, "a"));
        Assert.Equal(8, Reg(c, "b"));
        Assert.Equal(8, c.Device<RamModule>("dmem").ValueAt(100));
        Assert.Equal(0, Reg(c, "sp"));
        Assert.Equal(0, c.Device<RamModule>("pmem").ValueAt(100));
    }

    [Fact]
    public void CallAndReturnKeepTheReturnAddressOnTheDataStack()
    {
        var c = Run(@"
            CALL sub
            LBI  2
            HLT
    sub:    LDI  1
            RET");
        Assert.Equal((1, 2), (Reg(c, "a"), Reg(c, "b")));
        Assert.Equal(2, c.Device<RamModule>("dmem").ValueAt(4095));
        Assert.Equal(0, Reg(c, "sp"));
    }

    [Theory]
    [InlineData("JEQ", 5, 5, true)]
    [InlineData("JEQ", 5, 6, false)]
    [InlineData("JNE", 5, 6, true)]
    [InlineData("JCS", 3, 5, true)]
    [InlineData("JCC", 3, 5, false)]
    [InlineData("JMI", 3, 5, true)]
    [InlineData("JPL", 3, 5, false)]
    public void ConditionalJumpsFollowTheFlags(string jump, int a, int b, bool taken)
    {
        var c = Run($@"
            LDI  {a}
            CMPI {b}
            {jump} yes
            LBI  1
            HLT
    yes:    LBI  2
            HLT");
        Assert.Equal(taken ? 2 : 1, Reg(c, "b"));
    }

    [Fact]
    public void InReadsTheKeypadAcrossTheIoBridge()
    {
        var c = new Machine(Package().Machine, "\tIN\n\tHLT") { RecordHistory = false };
        c.Device<Keypad>("keys").Press(Keypad.Keys.Left);
        while (!c.IsHalted) c.SingleStep();
        Assert.Equal(4, Reg(c, "a"));
    }
}
