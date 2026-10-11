using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class RealTimeClockTests
{
    private sealed class ManualTime : TimeProvider
    {
        public long Milliseconds;
        public override long GetTimestamp() => Milliseconds;
        public override long TimestampFrequency => 1000;
    }

    private static (RealTimeClock Rtc, Register Cpu, Action<string[]> Tick) Rig(ManualTime time, int interval = 100)
    {
        var bus = new Bus();
        var cpu = new Register("CPU", "cpu", bus);
        var rtc = new RealTimeClock("RTC", "rtc", bus, interval, time);
        void Tick(params string[] lines)
        {
            foreach (var line in lines)
            {
                var parts = line.Split('.');
                (parts[0] == "rtc" ? (IBusDevice)rtc : cpu).Enable(parts[1]);
            }
            Clocking.Tick(new[] { bus }, new IBusDevice[] { cpu, rtc });
        }
        return (rtc, cpu, Tick);
    }

    [Fact]
    public void TheIntervalIsLoadedFromTheBusAndPutBackOnIt()
    {
        var (rtc, cpu, tick) = Rig(new ManualTime(), 1000);
        Assert.Equal(1000, rtc.Interval);
        cpu.Data = 250;
        tick(new[] { "cpu.output", "rtc.loadinterval" });
        Assert.Equal(250, rtc.Interval);
        cpu.Data = 0;
        tick(new[] { "rtc.output", "cpu.load" });
        Assert.Equal(250, cpu.Data);
        Assert.Equal(new[] { "loadinterval", "start", "stop", "output" }, rtc.SignalLines());
    }

    [Fact]
    public void ItRaisesAnInterruptOnceTheIntervalHasPassedAndNotBefore()
    {
        var time = new ManualTime();
        var (rtc, _, tick) = Rig(time, 100);
        tick(new string[0]);
        time.Milliseconds = 500;
        tick(new string[0]);
        Assert.False(rtc.TakeInterruptRequest());
        tick(new[] { "rtc.start" });
        Assert.True(rtc.Running);
        time.Milliseconds += 99;
        tick(new string[0]);
        Assert.False(rtc.TakeInterruptRequest());
        Assert.Equal(99, rtc.Elapsed);
        time.Milliseconds += 1;
        tick(new string[0]);
        Assert.True(rtc.TakeInterruptRequest());
        Assert.False(rtc.TakeInterruptRequest());
        Assert.Equal(1, rtc.Expired);
        tick(new[] { "rtc.stop" });
        time.Milliseconds += 1000;
        tick(new string[0]);
        Assert.False(rtc.TakeInterruptRequest());
        Assert.Equal(0, rtc.Elapsed);
    }

    [Fact]
    public void TheRequestsKeepInStepWithTheClock()
    {
        var time = new ManualTime();
        var (rtc, _, tick) = Rig(time, 100);
        tick(new[] { "rtc.start" });
        int requests = 0;
        for (int i = 0; i < 100; i++)
        {
            time.Milliseconds += 30;
            tick(new string[0]);
            if (rtc.TakeInterruptRequest()) requests++;
        }
        Assert.Equal(30, requests);
    }

    [Fact]
    public void AfterAPauseItRaisesOneInterruptAndStartsAfresh()
    {
        var time = new ManualTime();
        var (rtc, _, tick) = Rig(time, 100);
        tick(new[] { "rtc.start" });
        time.Milliseconds += 1050;
        tick(new string[0]);
        Assert.True(rtc.TakeInterruptRequest());
        Assert.Equal(1, rtc.Expired);
        time.Milliseconds += 99;
        tick(new string[0]);
        Assert.False(rtc.TakeInterruptRequest());
        time.Milliseconds += 1;
        tick(new string[0]);
        Assert.True(rtc.TakeInterruptRequest());
    }

    private sealed class CountingTime : TimeProvider
    {
        public long Microseconds, Reads;
        public override long GetTimestamp() { Reads++; return Microseconds; }
        public override long TimestampFrequency => 1_000_000;
    }

    [Fact]
    public void AFastMachineReadsTheTimeOnlyAboutEveryQuarterMillisecond()
    {
        var time = new CountingTime();
        var bus = new Bus();
        var rtc = new RealTimeClock("RTC", "rtc", bus, 10, time);
        rtc.Enable("start");
        Clocking.Tick(new[] { bus }, new IBusDevice[] { rtc });
        time.Reads = 0;
        const int MicrosecondsForTenIntervalsAndALateRead = 100_500;
        int requests = 0;
        for (int tick = 0; tick < MicrosecondsForTenIntervalsAndALateRead; tick++)
        {
            time.Microseconds++;
            Clocking.Tick(new[] { bus }, new IBusDevice[] { rtc });
            if (rtc.TakeInterruptRequest()) requests++;
        }
        Assert.Equal(10, requests);
        Assert.InRange(time.Reads, 300, 500);
    }

    [Fact]
    public void AnIntervalOfZeroNeverRaisesAnInterrupt()
    {
        var time = new ManualTime();
        var (rtc, _, tick) = Rig(time, 0);
        tick(new[] { "rtc.start" });
        time.Milliseconds += 10_000;
        tick(new string[0]);
        Assert.False(rtc.TakeInterruptRequest());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(20)]
    public void InAMachineItKeepsTimeWhateverTheSpeed(int ticksPerMillisecond)
    {
        var package = BuiltInPackages.Get("IRQ-16");
        var machine = MachineDefinition.FromJson(package.Machine.ToJson()
            .Replace("\"type\": \"timer\"", "\"type\": \"rtc\"")
            .Replace("\"period\": 20000", "\"interval\": 1000")
            .Replace("tick.loadperiod", "tick.loadinterval"));
        var time = new ManualTime();
        var source = package.Program("Three things at once").Source.Replace("TPERI    20000", "TPERI    1000   ");
        var c = new Machine(machine, source, DeviceRegistry.CreateDefault(time)) { RecordHistory = false };
        Assert.IsType<RealTimeClock>(c.Device("tick"));
        for (int ms = 0; ms < 5_200; ms++)
        {
            for (int i = 0; i < ticksPerMillisecond; i++) c.SingleStep();
            time.Milliseconds++;
        }
        Assert.StartsWith("time 05", c.Device<CharacterDisplay>("lcd").Line(0));
        Assert.Empty(c.MicrocodeWarnings);
    }
}
