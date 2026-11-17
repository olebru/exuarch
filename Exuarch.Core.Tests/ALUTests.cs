using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ALUTests
{
    private const byte Z = StatusRegister.ZeroFlag;
    private const byte C = StatusRegister.CarryFlag;
    private const byte V = StatusRegister.OverflowFlag;
    private const byte N = StatusRegister.NegativeFlag;

    private static (int result, int status) Run(string function, int a, int b, int statusBefore = 0)
    {
        var bus = new Bus();
        var rega = new Register("A", "rega", bus) { Data = a };
        var regb = new Register("B", "regb", bus) { Data = b };
        var sta = new StatusRegister("S", "regsta", bus) { Data = statusBefore };
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

    [Theory]
    [InlineData(2, 3, 0, 5, 0)]
    [InlineData(2, 3, C, 6, 0)]
    [InlineData(0xFFFF, 0, C, 0, Z | C)]
    [InlineData(0xFFFF, 0xFFFF, C, 0xFFFF, N | C)]
    [InlineData(0x7FFF, 0, C, 0x8000, N | V)]
    [InlineData(0x8000, 0x7FFF, C, 0, Z | C)]
    [InlineData(0, 0, Z | V | N, 0, Z)]
    public void AdcAddsTheCarryFromTheStatusRegister(int a, int b, int statusBefore, int expected, int flags)
    {
        var (result, status) = Run("adc", a, b, statusBefore);
        Assert.Equal(expected, result);
        Assert.Equal(flags, status);
    }

    [Theory]
    [InlineData(5, 3, 0, 2, 0)]
    [InlineData(5, 3, C, 1, 0)]
    [InlineData(3, 3, C, 0xFFFF, N | C)]
    [InlineData(0, 0xFFFF, C, 0, Z | C)]
    [InlineData(0x8000, 0x7FFF, C, 0, Z | V)]
    [InlineData(0x8000, 0, C, 0x7FFF, V)]
    [InlineData(4, 4, Z | N | V, 0, Z)]
    public void SbcSubtractsTheCarryFromTheStatusRegister(int a, int b, int statusBefore, int expected, int flags)
    {
        var (result, status) = Run("sbc", a, b, statusBefore);
        Assert.Equal(expected, result);
        Assert.Equal(flags, status);
    }

    [Theory]
    [InlineData("add", "adc", 0x0001FFFFu, 0x00000001u, 0x00020000u)]
    [InlineData("add", "adc", 0x1234FFFFu, 0x0000FFFFu, 0x1235FFFEu)]
    [InlineData("add", "adc", 0xFFFFFFFFu, 0x00000001u, 0x00000000u)]
    [InlineData("sub", "sbc", 0x00020000u, 0x00000001u, 0x0001FFFFu)]
    [InlineData("sub", "sbc", 0x12340000u, 0x0001FFFFu, 0x12320001u)]
    [InlineData("sub", "sbc", 0x00000000u, 0x00000001u, 0xFFFFFFFFu)]
    public void LowWordThenHighWordChainsThirtyTwoBitArithmetic(string low, string high, uint a, uint b, uint expected)
    {
        var bus = new Bus();
        var rega = new Register("A", "rega", bus);
        var regb = new Register("B", "regb", bus);
        var sta = new StatusRegister("S", "regsta", bus);
        var alu = new ALU("ALU", "alu", rega, regb, sta, bus);
        var result = new Register("R", "res", bus);
        bus.devices.Add(alu);
        bus.devices.Add(result);
        bus.devices.Add(sta);

        int Step(string function, uint x, uint y)
        {
            rega.Data = (int)(x & 0xFFFF);
            regb.Data = (int)(y & 0xFFFF);
            alu.Enable(function);
            result.Enable("load");
            bus.Clk();
            return result.Data;
        }

        int lowWord = Step(low, a, b);
        int highWord = Step(high, a >> 16, b >> 16);

        Assert.Equal(expected, (uint)highWord << 16 | (uint)lowWord);
        Assert.Equal(a < b && low == "sub" || (ulong)a + b > uint.MaxValue && low == "add", (sta.Data & C) != 0);
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
