using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class MoveMachineTests
{
    private static MachinePackage Package() => BuiltInPackages.Get("MOVE-16");

    private static Machine Run(string source, int limit = 2_000_000)
    {
        var c = new Machine(Package().Machine, source) { RecordHistory = false };
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < limit, "program did not halt"); }
        Assert.Empty(c.MicrocodeWarnings);
        return c;
    }
    private static int Reg(Machine c, string id) => c.Device<Register>(id).Data;

    [Fact]
    public void EveryInstructionIsAMove()
    {
        var microcode = Package().Machine.Decoder.Microcode;
        var sources = new[] { "IMM", "R0", "R1", "R2", "R3", "RES", "MEM", "KEYS" };
        Assert.All(microcode.Instructions, i =>
        {
            var parts = i.Mnemonic.Split("_TO_");
            Assert.Equal(2, parts.Length);
            Assert.Contains(parts[0], sources);
            Assert.Equal(parts[0] == "IMM" ? 1 : 0, i.Operands);
        });
        Assert.True(microcode.Instructions.Count > 200);
        Assert.Empty(MicrocodeValidator.Validate(microcode, Package().Machine));
    }

    [Fact]
    public void HelloPrintsWithNothingButMoves()
    {
        var c = Run(Package().Program("Hello, world with nothing but moves").Source);
        Assert.Equal("Nothing here but moves!", c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd());
    }

    [Fact]
    public void CountReachesNinetyNine()
    {
        var c = Run(Package().Program("Count from 00 to 99").Source);
        var text = c.Device<CharacterDisplay>("lcd").Text.Replace("\n", "");
        Assert.EndsWith("96 97 98 99", text.TrimEnd());
    }

    [Fact]
    public void XorTextureIsXOrYDrawnAtTwiceTheSize()
    {
        var c = Run(Package().Program("XOR texture").Source, 10_000_000);
        var fb = c.Device<Framebuffer>("fb");
        for (int y = 0; y < 128; y += 9)
        {
            for (int x = 0; x < 128; x += 7)
            {
                int expected = ((x ^ y) << 9) & 0xFFFF;
                Assert.Equal(expected, fb.Pixels[(112 + 2 * y) * Framebuffer.Width + 192 + 2 * x]);
                Assert.Equal(expected, fb.Pixels[(113 + 2 * y) * Framebuffer.Width + 193 + 2 * x]);
            }
        }
        Assert.Equal(0, fb.Pixels[111 * Framebuffer.Width + 192]);
    }

    [Fact]
    public void MovingIntoTriggersComputes()
    {
        var c = Run(@"
            IMM_TO_A   40
            IMM_TO_ADD 2
            RES_TO_R0
            RES_TO_A
            IMM_TO_SHL 2
            RES_TO_R1
            IMM_TO_A   0x0F0F
            IMM_TO_XOR 0x00FF
            RES_TO_R2
            R0_TO_HALT");
        Assert.Equal((42, 168, 0x0FF0), (Reg(c, "r0"), Reg(c, "r1"), Reg(c, "r2")));
    }

    [Fact]
    public void MemoryIsReachedThroughMar()
    {
        var c = Run(@"
            IMM_TO_MAR 200
            IMM_TO_MEM 7
            MEM_TO_R0
            IMM_TO_MAR 201
            R0_TO_MEM
            MEM_TO_A
            IMM_TO_ADD 1
            RES_TO_MEM
            MEM_TO_R1
            R0_TO_HALT");
        Assert.Equal((7, 8), (Reg(c, "r0"), Reg(c, "r1")));
        Assert.Equal(7, c.Device<RamModule>("mem").ValueAt(200));
        Assert.Equal(8, c.Device<RamModule>("mem").ValueAt(201));
    }

    [Fact]
    public void JumpsLandOnTheirTargetAndCallsReturn()
    {
        var c = Run(@"
            IMM_TO_JMP  over
            IMM_TO_R0   99
    over:   IMM_TO_CALL sub
            IMM_TO_R2   2
            R0_TO_HALT
    sub:    IMM_TO_R1   1
            R3_TO_JMP");
        Assert.Equal((0, 1, 2), (Reg(c, "r0"), Reg(c, "r1"), Reg(c, "r2")));
    }

    [Theory]
    [InlineData("IMM_TO_JZ", 5, 5, true)]
    [InlineData("IMM_TO_JZ", 5, 6, false)]
    [InlineData("IMM_TO_JNZ", 5, 6, true)]
    [InlineData("IMM_TO_JNZ", 5, 5, false)]
    [InlineData("IMM_TO_JN", 3, 5, true)]
    [InlineData("IMM_TO_JN", 5, 3, false)]
    [InlineData("IMM_TO_JC", 3, 5, true)]
    [InlineData("IMM_TO_JC", 5, 5, false)]
    public void ConditionalJumpsFollowTheFlags(string jump, int a, int b, bool taken)
    {
        var c = Run($@"
            IMM_TO_A   {a}
            IMM_TO_CMP {b}
            {jump}     yes
            IMM_TO_R0  1
            R0_TO_HALT
    yes:    IMM_TO_R0  2
            R0_TO_HALT");
        Assert.Equal(taken ? 2 : 1, Reg(c, "r0"));
    }

    [Fact]
    public void RegisterJumpTargetsWork()
    {
        var c = Run(@"
            IMM_TO_R1  target
            IMM_TO_A   0
            IMM_TO_CMP 0
            R1_TO_JZ
            IMM_TO_R0  9
    target: R0_TO_HALT");
        Assert.Equal(0, Reg(c, "r0"));
    }

    [Fact]
    public void KeysAreASource()
    {
        var c = new Machine(Package().Machine, "\tKEYS_TO_R0\n\tR0_TO_HALT") { RecordHistory = false };
        c.Device<Keypad>("keys").Press(Keypad.Keys.Up);
        c.Device<Keypad>("keys").Press(Keypad.Keys.Space);
        while (!c.IsHalted) c.SingleStep();
        Assert.Equal(17, Reg(c, "r0"));
    }
}
