using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Devices;
using Exuarch.Web.Run;

namespace Exuarch.Web.Tests;

public class ViewObserverTests
{
    private sealed class Sink : IAnalyticsSink
    {
        private readonly HashSet<string> once = new HashSet<string>();
        public readonly List<string> Sent = new List<string>();

        public bool Once(string key) => once.Add(key);

        public Task Send(string name, params (string Key, string Value)[] data)
        {
            Sent.Add($"{name} {string.Join(" ", data.Select(d => $"{d.Key}={d.Value}"))}".Trim());
            return Task.CompletedTask;
        }

        public Task Milestone(string name, string once, params (string Key, string Value)[] data) => Send("milestone " + name, data);

        public Task Summary(string key, string value) => Send("summary", (key, value));
    }

    private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AMachineIsCountedTheFirstTimeAndEachNewTabOnce()
    {
        var sink = new Sink();
        var observer = new FirstSeenObserver();
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Tab = "Hardware" }, Start, sink);
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Tab = "Run" }, Start, sink);
        await observer.Observe(new Analytics.View { Machine = "mine", Tab = "Run" }, Start, sink);
        Assert.Equal(new[] { "machine machine=BYOC-16", "summary tabs used=1", "summary tabs used=2", "machine machine=own" }, sink.Sent);
    }

    [Fact]
    public async Task APageIsReadAfterHalfAMinuteOpen()
    {
        var sink = new Sink();
        var observer = new PageReadObserver();
        var reference = new Analytics.View { Page = ("reference", "alu") };
        await observer.Observe(reference, Start, sink);
        await observer.Observe(reference, Start.AddSeconds(29), sink);
        Assert.Empty(sink.Sent);
        await observer.Observe(reference, Start.AddSeconds(30), sink);
        await observer.Observe(reference, Start.AddSeconds(40), sink);
        Assert.Equal(new[] { "read page=reference/alu" }, sink.Sent);
        // Another page starts the half minute again.
        await observer.Observe(new Analytics.View { Page = ("reference", "nonsense") }, Start.AddSeconds(41), sink);
        await observer.Observe(new Analytics.View { Page = ("reference", "nonsense") }, Start.AddSeconds(80), sink);
        Assert.Single(sink.Sent);
    }

    [Fact]
    public async Task ErrorsThatStayWhileBusyAreWhereSomeoneGotStuck()
    {
        var sink = new Sink();
        var observer = new StuckObserver();
        var time = Start;
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = "program", Active = true }, time, sink);
        // Idle time does not count, and no more than ten seconds between two looks.
        for (int i = 0; i < 20; i++) await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = "program", Active = false }, time = time.AddSeconds(5), sink);
        for (int i = 0; i < 11; i++) await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = "program", Active = true }, time = time.AddSeconds(30), sink);
        Assert.Empty(sink.Sent);
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = "program", Active = true }, time = time.AddSeconds(5), sink);
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = "program", Active = true }, time = time.AddSeconds(5), sink);
        Assert.Equal(new[] { "stuck where=program machine=BYOC-16" }, sink.Sent);
        await observer.Observe(new Analytics.View { Machine = "BYOC-16", Errors = null, Active = true }, time.AddSeconds(5), sink);
        Assert.Equal("unstuck where=program", sink.Sent[^1]);
    }
}

public class ViewModelTests
{
    private static Machine Build(string package, int program = 0)
    {
        var built = BuiltInPackages.Get(package);
        return new Machine(built.Machine, built.Programs[program].Source);
    }

    [Fact]
    public void TheMemoryMarksShowTheProgramCounterTheInstructionAndTheWrites()
    {
        var machine = Build("BYOC-16");
        for (int i = 0; i < 3; i++) machine.StepInstruction();
        var memory = machine.Definition.ProgramMemory;
        var module = (MemoryModule)machine.Device(memory);
        var marks = MemoryMarks.Build(machine, memory, -1);
        var current = machine.InstructionAt(machine.CurrentInstructionAddress.Value);
        Assert.Equal(current.Address, marks.CurrentStart);
        Assert.Equal(current.Address + current.Cells.Length, marks.CurrentEnd);
        Assert.NotEmpty(marks.ProgramCounters);
        Assert.Contains(" current", marks.Css(current.Address, module));
        Assert.Contains(" mar", marks.Css(module.memoryAddress, module));
        // Another memory has no program to mark.
        Assert.Empty(MemoryMarks.Build(machine, "elsewhere", -1).ProgramCounters);
    }

    [Fact]
    public void TheListingMarksTheCurrentInstructionAndWhereABreakpointCanGo()
    {
        var machine = Build("BYOC-16");
        machine.StepInstruction();
        var rows = ListingRow.Of(machine).ToList();
        var current = rows.Single(r => r.Css.Contains("current"));
        Assert.Equal($"listing-{machine.CurrentInstructionAddress}", current.Id);
        Assert.Equal(current.Address, current.Breakpoint);
        Assert.All(rows.Where(r => r.Css.EndsWith("data")), r => Assert.Null(r.Breakpoint));
        Assert.True(ListingRow.LabelChars(machine) >= 1);
    }

    [Fact]
    public void EachKindOfDeviceIsShownAsItsNearestKnownClass()
    {
        var machine = Build("GPU-16");
        foreach (var device in machine.Devices)
        {
            var view = DeviceViews.For(device);
            Assert.NotNull(view.Summary);
            // A register shows its bits, and its flags register does not.
            Assert.Equal(device is DualPortRegister or InstructionRegister or Register and not StatusRegister, view.Word(device) != null);
        }
        Assert.True(DeviceViews.For(new RamModule("ram", "ram", new Bus(), 16)).Stacked);
    }

    [Fact]
    public void ProgramNeedsAreMetByTheSliderOrMaxSpeed()
    {
        var speed = new RunSpeed();
        var needs = new ProgramNeeds { MinHz = 250_000 };
        Assert.True(NeedChecks.SpeedTooLow(needs, speed));
        NeedChecks.MeetMinHz(250_000, speed);
        Assert.False(NeedChecks.SpeedTooLow(needs, speed));
        NeedChecks.MeetMinHz(5_000_000, speed);
        Assert.True(speed.Max);
        var exact = new ProgramNeeds { ExactHz = 100 };
        Assert.True(NeedChecks.SpeedNotExact(exact, speed));
        NeedChecks.MeetExactHz(100, speed);
        Assert.False(speed.Max);
        Assert.False(NeedChecks.SpeedNotExact(exact, speed));
        Assert.False(NeedChecks.SpeedTooLow(null, speed));
    }
}
