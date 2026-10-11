using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ALUTests
{
    private const byte Z = StatusRegister.ZeroFlag;
    private const byte C = StatusRegister.CarryFlag;
    private const byte V = StatusRegister.OverflowFlag;
    private const byte N = StatusRegister.NegativeFlag;

    private static (int result, int status) Run(string function, int a, int b)
    {
        var bus = new Bus();
        var rega = new Register("A", "rega", bus) { Data = a };
        var regb = new Register("B", "regb", bus) { Data = b };
        var sta = new StatusRegister("S", "regsta", bus);
        var alu = new ALU("ALU", "alu", rega, regb, sta, bus);
        var result = new Register("R", "res", bus);
        bus.devices.Add(alu);
        bus.devices.Add(result);
        bus.devices.Add(sta);

        alu.Enable(function);
        if (alu.IsOutputEnabled()) result.Enable("load");
        bus.Clk();
        return (result.Data, sta.Data);
    }

    [Theory]
    [InlineData(2, 3, 5, 0)]
    [InlineData(0, 0, 0, Z)]
    [InlineData(0xFFFF, 1, 0, Z | C)]
    [InlineData(40000, 30000, 4464, C)]
    [InlineData(0x7FFF, 1, 0x8000, N | V)]
    [InlineData(0x8000, 0x8000, 0, Z | C | V)]
    [InlineData(0xFFFF, 0, 0xFFFF, N)]
    [InlineData(0xFFFF, 0xFFFF, 0xFFFE, N | C)]
    public void Add(int a, int b, int expected, int flags)
    {
        var (result, status) = Run("add", a, b);
        Assert.Equal(expected, result);
        Assert.Equal(flags, status);
    }

    [Theory]
    [InlineData(5, 3, 2, 0)]
    [InlineData(4, 4, 0, Z)]
    [InlineData(3, 5, 0xFFFE, N | C)]
    [InlineData(0x8000, 1, 0x7FFF, V)]
    [InlineData(0, 0x8000, 0x8000, N | C | V)]
    [InlineData(5, 0xFFFF, 6, C)]
    [InlineData(0xFFFF, 1, 0xFFFE, N)]
    public void Sub(int a, int b, int expected, int flags)
    {
        var (result, status) = Run("sub", a, b);
        Assert.Equal(expected, result);
        Assert.Equal(flags, status);
    }

    [Theory]
    [InlineData(4, 4, Z)]
    [InlineData(3, 5, N | C)]
    [InlineData(9, 2, 0)]
    [InlineData(0xFFFF, 1, N)]
    public void CmpSetsSameFlagsAsSubWithoutDrivingBus(int a, int b, int flags)
    {
        var (result, status) = Run("cmp", a, b);
        Assert.Equal(0, result);
        Assert.Equal(flags, status);
    }

    [Theory]
    [InlineData(3, 5, true, true)]
    [InlineData(5, 3, false, false)]
    [InlineData(0xFFFF, 1, false, true)]
    [InlineData(1, 0xFFFF, true, false)]
    [InlineData(0x8000, 1, false, true)]
    [InlineData(0x7FFF, 0x8000, true, false)]
    public void CmpComparesUnsignedWithCAndSignedWithNAndV(int a, int b, bool below, bool less)
    {
        var (_, status) = Run("cmp", a, b);
        Assert.Equal(below, (status & C) != 0);
        Assert.Equal(less, ((status & N) != 0) != ((status & V) != 0));
    }

    [Theory]
    [InlineData("and", 0x8001, 0x8000, 0x8000, N)]
    [InlineData("orr", 0, 0, 0, Z)]
    [InlineData("eor", 0x8000, 0x8000, 0, Z)]
    [InlineData("lsl", 0x4001, 1, 0x8002, N)]
    [InlineData("lsl", 0x8001, 1, 0x0002, C)]
    [InlineData("lsr", 0x0003, 1, 0x0001, C)]
    public void LogicAndShiftsSetNFromTheTopBit(string op, int a, int b, int expected, int flags)
    {
        var (result, status) = Run(op, a, b);
        Assert.Equal(expected, result);
        Assert.Equal(flags, status);
    }

    [Fact]
    public void StatusRegisterFlagProperties()
    {
        var sta = new StatusRegister("S", "regsta", new Bus());
        sta.Data = N | Z;
        Assert.True(sta.Negative8);
        Assert.True(sta.Zero1);
        Assert.False(sta.Carry2);
        Assert.False(sta.Overflow4);
    }
}
