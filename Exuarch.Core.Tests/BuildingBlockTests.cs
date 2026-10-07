using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

// The parts devices, validators and the machine are built from.
public class BuildingBlockTests
{
    // A clock the test moves by hand, in ticks of its own.
    private sealed class ManualTime : TimeProvider
    {
        public long Now;
        public long Frequency = 4000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => Frequency;
    }

    [Fact]
    public void AControlLineTableEnablesALineByNameOrBoundOnce()
    {
        var enabled = new List<string>();
        var table = new ControlLineTable().Add("a", () => enabled.Add("a")).Add("b", () => enabled.Add("b"));
        table.Enable("b");
        table.Find("a")();
        Assert.Equal(new[] { "b", "a" }, enabled);
        Assert.Equal(new[] { "a", "b" }, table.Names());
        Assert.Null(table.Find("c"));
        Assert.Contains("unknown function: c", Assert.Throws<Exception>(() => table.Enable("c")).Message);
    }

    [Fact]
    public void BindingALineOfADeviceWithoutATableCallsEnable()
    {
        var register = new Register("R", "r", new Bus());
        Assert.Same(register.ControlLines.Find("inc"), ControlLineTable.Bind(register, "inc"));
        var listener = new Listener();
        ControlLineTable.Bind(listener, "go")();
        Assert.Equal("go", listener.Enabled);
        // A line the device does not have fails when it is enabled, as Enable does.
        var missing = ControlLineTable.Bind(register, "nosuch");
        Assert.Throws<Exception>(() => missing());
    }

    private sealed class Listener : IBusDevice
    {
        public string Enabled;
        public void Drive() { }
        public void Latch() { }
        public string DisplayName() => "listener";
        public void Enable(string function) { Enabled = function; }
        public string ID() => "listener";
        public bool IsOutputEnabled() => false;
        public List<string> SignalLines() => new List<string>();
    }

    [Fact]
    public void LatchedLinesActInTheOrderTheyWereAddedOncePerTick()
    {
        var acted = new List<int>();
        var lines = new LatchedLines();
        var first = lines.Add(() => acted.Add(1));
        var second = lines.Add(() => acted.Add(2));
        lines.Add(() => acted.Add(3));
        second();
        first();
        second();
        lines.Latch();
        Assert.Equal(new[] { 1, 2 }, acted);
        lines.Latch();
        Assert.Equal(new[] { 1, 2 }, acted);
    }

    [Theory]
    [InlineData("10x0x", 0b01000, true)]
    [InlineData("10x0x", 0b11000, true)]
    [InlineData("10x0x", 0b01001, false)]
    [InlineData("xxxx1", 0b10000, true)]
    [InlineData("xxxx1", 0b01111, false)]
    public void AFlagConditionMatchesTheStatusBitsItTests(string pattern, int status, bool matches)
    {
        Assert.Equal(matches, FlagCondition.FromPattern(pattern).Matches(status));
    }

    [Fact]
    public void AFlagConditionKeepsEachFlagAsTheJsonSeesIt()
    {
        var condition = new FlagCondition { N = true, Z = false };
        Assert.Equal("1xx0x", FlagCondition.ToPattern(condition));
        Assert.Equal("N=1 Z=0", condition.ToString());
        Assert.Equal(StatusRegister.NegativeFlag | StatusRegister.ZeroFlag, condition.Care);
        condition.N = null;
        Assert.Null(condition.N);
        Assert.False(condition.Z);
        condition.Z = null;
        Assert.True(condition.IsAlways);
        Assert.Equal("always", condition.ToString());
        Assert.Null(FlagCondition.FromPattern("xxxxx"));
    }

    [Fact]
    public void TheAluOperationsAreTheAlusLinesInOrder()
    {
        var alu = new ALU("ALU", "alu", new Register("A", "a", new Bus()), new Register("B", "b", new Bus()), new StatusRegister("S", "s", new Bus()), new Bus());
        Assert.Equal(alu.SignalLines(), AluOperation.All.Select(o => o.Name));
        Assert.Equal(new[] { "cmp" }, AluOperation.All.Where(o => !o.DrivesBus).Select(o => o.Name));
    }

    [Theory]
    [InlineData("add", 0x7FFF, 1, 0x8000, StatusRegister.OverflowFlag | StatusRegister.NegativeFlag)]
    [InlineData("add", 0xFFFF, 1, 0, StatusRegister.CarryFlag | StatusRegister.ZeroFlag)]
    [InlineData("sub", 1, 2, 0xFFFF, StatusRegister.CarryFlag | StatusRegister.NegativeFlag)]
    [InlineData("eor", 0xF0F0, 0xFFFF, 0x0F0F, 0)]
    [InlineData("lsl", 0x8001, 1, 2, StatusRegister.CarryFlag)]
    [InlineData("lsr", 3, 1, 1, StatusRegister.CarryFlag)]
    public void AnAluOperationGivesItsResultAndFlags(string name, int x, int y, int result, int status)
    {
        var operation = AluOperation.All.Single(o => o.Name == name);
        Assert.Equal(result, operation.Run(x, y, out int flags));
        Assert.Equal(status, flags);
    }

    // Looking costs, so the sampler looks less often while ticks come quickly and every tick when they are slow.
    [Fact]
    public void TheTimeSamplerLooksAboutEveryQuarterOfAMillisecond()
    {
        var time = new ManualTime();
        var sampler = new TimeSampler(time);
        Assert.Equal(0, sampler.Restart());
        // A quarter of a millisecond is one timestamp, and ten ticks take one: the sampler settles on about ten ticks.
        int looks = 0;
        for (int tick = 0; tick < 200; tick++)
        {
            if (tick % 10 == 0) time.Now++;
            if (sampler.Look(out long now))
            {
                looks++;
                Assert.Equal(time.Now, now);
            }
        }
        Assert.InRange(sampler.TicksBetweenLooks, 5, 20);
        Assert.InRange(looks, 10, 40);

        // When no time passes between looks, it waits twice as long each time, up to a limit.
        var still = new TimeSampler(new ManualTime());
        still.Restart();
        for (int tick = 0; tick < 1_000_000; tick++) still.Look(out _);
        Assert.Equal(TimeSampler.MostTicksBetweenLooks, still.TicksBetweenLooks);
    }

    [Fact]
    public void ATriangleCoversThePixelsWhoseCentresAreInside()
    {
        // Corners (0,0), (4,0) and (0,4), all white at depth 100.
        var words = new[] { 0, 0, 100, 0xFFFF, 4, 0, 100, 0xFFFF, 0, 4, 100, 0xFFFF };
        var triangle = TriangleSetup.From(words);
        Assert.True(triangle.Span(0, out int first, out int last));
        // The pixel centred on the long edge belongs to the triangle on the other side of it.
        Assert.Equal((0, 2), (first, last));
        Assert.True(triangle.Span(2, out first, out last));
        Assert.Equal((0, 0), (first, last));
        Assert.False(triangle.Span(3, out _, out _));
        Assert.Equal((0xFFFF, 100), triangle.Fragment(1, 1));
        // The same corners the other way round make the same triangle; a line has no area.
        var reversed = TriangleSetup.From(new[] { 0, 0, 100, 0xFFFF, 0, 4, 100, 0xFFFF, 4, 0, 100, 0xFFFF });
        Assert.True(reversed.Span(0, out first, out last));
        Assert.Equal((0, 2), (first, last));
        Assert.Null(TriangleSetup.From(new[] { 0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0 }));
    }

    [Fact]
    public void RenamingADeviceMovesItsRoles()
    {
        var machine = MachineTemplates.Minimal("Mine").Machine;
        var status = machine.Decoder.Status;
        machine.RenameDevice(status, "flags");
        Assert.Equal("flags", machine.Decoder.Status);
        machine.RemoveDevice("flags");
        Assert.Null(machine.Decoder.Status);
        // Without a decoder there are no decoder roles to move.
        machine.Decoder = null;
        machine.RemoveDevice(machine.Halt);
        Assert.Null(machine.Halt);
    }
}
