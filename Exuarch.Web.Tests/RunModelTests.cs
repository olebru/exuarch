using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Run;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Exuarch.Web.Tests;

// Time that only moves when it is told to.
internal sealed class ManualTime : TimeProvider
{
    private long microseconds;

    public override long TimestampFrequency => 1_000_000;
    public override long GetTimestamp() => microseconds;
    public void Advance(double milliseconds) => microseconds += (long)Math.Round(milliseconds * 1000);
}

// A browser that answers every call with nothing, and remembers what it was asked.
internal sealed class RecordingJs : IJSRuntime
{
    public readonly List<(string Identifier, object[] Args)> Calls = new List<(string, object[])>();
    public Func<string, object[], object> Answer = (_, _) => null;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object[] args)
    {
        Calls.Add((identifier, args));
        return new ValueTask<TValue>((TValue)(Answer(identifier, args) ?? default(TValue)));
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object[] args) => InvokeAsync<TValue>(identifier, args);
}

public class ClockPacingTests
{
    private sealed class Frame
    {
        public readonly ManualTime Time = new ManualTime();
        public int Ticks;
        public long Swaps;
        public readonly List<bool> Recording = new List<bool>();
        // Each tick takes this long, and the run stops at this tick.
        public double TickMilliseconds;
        public int StopAt = int.MaxValue;
        public int SwapEvery;

        public RunFrame Make() => new RunFrame(new ElapsedClock(Time), Tick, Recording.Add, () => Swaps);

        private bool Tick()
        {
            Ticks++;
            Time.Advance(TickMilliseconds);
            if (SwapEvery > 0 && Ticks % SwapEvery == 0) Swaps++;
            return Ticks < StopAt;
        }
    }

    [Fact]
    public void ASlowClockRunsTheTicksThatAreDueAndRecordsEveryOne()
    {
        var test = new Frame();
        var frame = test.Make();
        frame.Start(0.5);
        Assert.True(new FixedHzPacer(16).RunFrame(frame));
        // Half a second at 16 Hz is 8 ticks, but no more than a quarter of a second is made up for.
        Assert.Equal(5, test.Ticks);
        Assert.Equal(new[] { true, true }, test.Recording);
        Assert.Equal(16, new FixedHzPacer(16).WaitMilliseconds(3));
        Assert.Equal(1, new FixedHzPacer(1000).WaitMilliseconds(3));
    }

    [Fact]
    public void AFastClockRecordsOnlyTheLastTickOfTheFrame()
    {
        var test = new Frame();
        var frame = test.Make();
        frame.Start(0.01);
        Assert.True(new FixedHzPacer(1000).RunFrame(frame));
        Assert.Equal(10, test.Ticks);
        Assert.Equal(0, frame.Owed);
        Assert.Equal(new[] { false, true }, test.Recording);
    }

    [Fact]
    public void ATickThatStopsTheRunEndsTheFrame()
    {
        var test = new Frame { StopAt = 3 };
        var frame = test.Make();
        frame.Start(1);
        Assert.False(new FixedHzPacer(100).RunFrame(frame));
        Assert.Equal(3, test.Ticks);
    }

    [Fact]
    public void MaxSpeedRunsBatchesForAFrameThenOneRecordedTick()
    {
        var test = new Frame { TickMilliseconds = 0.05 };
        var frame = test.Make();
        frame.Owed = 3;
        frame.Start(0);
        var pacer = new MaxSpeedPacer();
        Assert.True(pacer.RunFrame(frame));
        // 25 ms of 0.05 ms ticks is two batches of 256, and the tick that is shown.
        Assert.Equal(513, test.Ticks);
        Assert.Equal(0, frame.Owed);
        Assert.Equal(new[] { false, true }, test.Recording);
        Assert.Null(pacer.WaitMilliseconds(30));
        Assert.True(pacer.RationsRedraws);
    }

    [Fact]
    public void MaxSpeedStopsWhenADoubleBufferedScreenSwaps()
    {
        var test = new Frame { SwapEvery = 100 };
        var frame = test.Make();
        frame.Start(0);
        Assert.True(new MaxSpeedPacer().RunFrame(frame));
        Assert.True(frame.Swapped);
        Assert.Equal(257, test.Ticks);
    }

    [Fact]
    public void TheViewIsDrawnLessOftenTheLongerDrawingItTakes()
    {
        var time = new ManualTime();
        var throttle = new RedrawThrottle(time);
        throttle.StartFrame();
        Assert.True(throttle.ShouldDraw(false, rationed: false));
        Assert.False(throttle.ShouldDraw(false, rationed: true));
        Assert.True(throttle.ShouldDraw(true, rationed: true));
        time.Advance(30);
        Assert.True(throttle.ShouldDraw(false, rationed: true));
        throttle.Draw(() => time.Advance(10));
        throttle.StartFrame();
        Assert.Equal(90, throttle.BudgetMilliseconds);
        time.Advance(100);
        throttle.Draw(() => time.Advance(100));
        throttle.StartFrame();
        Assert.Equal(RedrawThrottle.LongestMilliseconds, throttle.BudgetMilliseconds);
    }
}

public class SpeedMeterTests
{
    [Fact]
    public void ARunIsSampledEveryQuarterOfASecond()
    {
        var meter = new SpeedMeter();
        meter.StartRun(0);
        meter.Sample(0.1, 10);
        Assert.False(meter.HasSamples);
        meter.Sample(0.25, 25);
        meter.Sample(0.5, 75);
        Assert.Equal(new[] { 100.0, 200.0 }, meter.Samples.Select(s => s.Hz));
        Assert.Equal(200, meter.Current);
        Assert.Equal(200, meter.Peak);
        // Too short a rest to say anything, then one that does.
        meter.FinishRun(0.52, 80);
        Assert.Equal(2, meter.Samples.Count);
        meter.FinishRun(0.6, 83);
        Assert.Equal(3, meter.Samples.Count);
        Assert.Equal(5, meter.Span);
    }

    [Fact]
    public void OnlyTheLastMinuteOfRunningIsKept()
    {
        var meter = new SpeedMeter();
        meter.StartRun(0);
        for (int i = 1; i <= 300; i++) meter.Sample(i * 0.25, i);
        Assert.Equal(SpeedMeter.WindowSeconds, meter.Span);
        Assert.True(meter.Samples[0].Seconds >= 75 - SpeedMeter.WindowSeconds);
        meter.Clear();
        Assert.False(meter.HasSamples);
    }

    [Fact]
    public void TheChartTopsOutAtARoundNumberAboveThePeakAndTheTarget()
    {
        var meter = new SpeedMeter();
        meter.StartRun(0);
        meter.Sample(0.25, 25);
        Assert.Equal(200, new SpeedChart(meter, 16, max: false).Max);
        Assert.Equal(2000, new SpeedChart(meter, 1500, max: false).Max);
        Assert.Equal(200, new SpeedChart(meter, 1500, max: true).Max);
        Assert.Equal(10, new SpeedChart(new SpeedMeter(), 0, max: true).Max);
    }
}

public class RunInputTests
{
    [Fact]
    public async Task TheFirstShortcutThatMatchesRuns()
    {
        var ran = new List<string>();
        var keys = new KeyMap()
            .On("Shift+ArrowRight", () => ran.Add("instruction"))
            .On("ArrowRight", () => ran.Add("tick"))
            .On("m", () => ran.Add("max"))
            .On("r", () => ran.Add("reset"));
        await keys.Dispatch(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        await keys.Dispatch(new KeyboardEventArgs { Key = "ArrowRight" });
        await keys.Dispatch(new KeyboardEventArgs { Key = "M", ShiftKey = true });
        await keys.Dispatch(new KeyboardEventArgs { Key = "R" });
        await keys.Dispatch(new KeyboardEventArgs { Key = "x" });
        Assert.Equal(new[] { "instruction", "tick", "max", "reset" }, ran);
    }

    [Fact]
    public void ACapturedKeypadTakesTheArrowsAndSpaceUntilEscape()
    {
        var capture = new KeypadCapture();
        var keypad = new Keypad("keypad", "keys", new Bus());
        Assert.False(capture.KeyDown("ArrowUp"));
        capture.Capture(keypad);
        Assert.True(capture.KeyDown("ArrowUp"));
        Assert.True(capture.KeyDown(" "));
        Assert.Equal((int)(Keypad.Keys.Up | Keypad.Keys.Space), (int)keypad.Held);
        Assert.False(capture.KeyDown("m"));
        capture.KeyUp("ArrowUp");
        Assert.Equal(Keypad.Keys.Space, keypad.Held);
        Assert.True(capture.KeyDown("Escape"));
        Assert.Null(capture.Keypad);
        Assert.Equal((Keypad.Keys)0, keypad.Held);
    }
}

public class RunLayoutTests
{
    private static List<RunPanel> Panels() => new List<RunPanel>
    {
        new RunPanel { Id = "clock" },
        new RunPanel { Id = "device:fb", DefaultDock = Dock.Right },
        new RunPanel { Id = "program" },
        new RunPanel { Id = "now" },
        new RunPanel { Id = "memory", DefaultDock = Dock.Bottom },
        new RunPanel { Id = "decoder", DefaultDock = Dock.Bottom },
        new RunPanel { Id = "trace", DefaultDock = Dock.Bottom },
    };

    private static string[] Ids(Dictionary<Dock, List<RunPanel>> docks, Dock dock) => docks[dock].Select(p => p.Id).ToArray();

    [Fact]
    public void PanelsStartWhereTheyBelongAndGoWhereTheyWerePut()
    {
        var layout = new RunLayout();
        var panels = Panels();
        var docks = layout.Place(panels);
        Assert.Equal(new[] { "clock", "program", "now" }, Ids(docks, Dock.Left));
        Assert.Equal(new[] { "device:fb" }, Ids(docks, Dock.Right));
        Assert.Equal(new[] { "memory", "decoder", "trace" }, Ids(docks, Dock.Bottom));
        Assert.Equal("memory", layout.ActiveTab(Dock.Bottom, docks[Dock.Bottom]).Id);

        layout.Load(new SavedLayout { Right = new List<string> { "gone", "trace" }, BottomRight = new List<string> { "decoder" } });
        docks = layout.Place(panels);
        Assert.Equal(new[] { "trace", "device:fb" }, Ids(docks, Dock.Right));
        // Not split, the second group's tabs are with the first.
        Assert.Equal(new[] { "memory", "decoder" }, Ids(docks, Dock.Bottom));
        Assert.Empty(docks[Dock.Bottom2]);
    }

    [Fact]
    public void SplittingMovesTheLastTabToASecondGroupAndJoiningPutsItBack()
    {
        var layout = new RunLayout();
        var panels = Panels();
        layout.ToggleSplit(layout.Place(panels));
        var docks = layout.Place(panels);
        Assert.Equal(new[] { "memory", "decoder" }, Ids(docks, Dock.Bottom));
        Assert.Equal(new[] { "trace" }, Ids(docks, Dock.Bottom2));
        Assert.Equal("trace", layout.ActiveTab(Dock.Bottom2, docks[Dock.Bottom2]).Id);
        layout.ToggleSplit(docks);
        Assert.Equal(new[] { "memory", "decoder", "trace" }, Ids(layout.Place(panels), Dock.Bottom));
    }

    [Fact]
    public void ADroppedPanelGoesInFrontOfThePanelItIsDroppedOn()
    {
        var layout = new RunLayout();
        var panels = Panels();
        layout.DragOver(new Place(Dock.Left, "now"));
        Assert.False(layout.DropsBefore(Dock.Left, "now"));
        layout.StartDrag("trace");
        layout.DragOver(new Place(Dock.Left, "program"));
        Assert.True(layout.DropsBefore(Dock.Left, "program"));
        Assert.True(layout.Drop(new Place(Dock.Left, "program"), layout.Place(panels)));
        Assert.Null(layout.Dragged);
        Assert.False(layout.DropsBefore(Dock.Left, "program"));
        Assert.Equal(new[] { "clock", "trace", "program", "now" }, Ids(layout.Place(panels), Dock.Left));

        // Dropped below, last, it is the tab on show; dropped on itself, nothing moves.
        layout.StartDrag("clock");
        Assert.True(layout.Drop(new Place(Dock.Bottom, null), layout.Place(panels)));
        var docks = layout.Place(panels);
        Assert.Equal(new[] { "memory", "decoder", "clock" }, Ids(docks, Dock.Bottom));
        Assert.Equal("clock", layout.ActiveTab(Dock.Bottom, docks[Dock.Bottom]).Id);
        layout.StartDrag("now");
        Assert.False(layout.Drop(new Place(Dock.Left, "now"), docks));
    }

    [Fact]
    public void FoldingIsKeptAsAListAndResetOpensEveryDock()
    {
        var layout = new RunLayout();
        Assert.False(layout.FoldSaved(null));
        Assert.True(layout.FoldSaved("now,left"));
        layout.Toggle("bottom");
        layout.Toggle("now");
        Assert.Equal("bottom,left", layout.CollapsedList);
        Assert.Equal(new[] { "left", "bottom" }, layout.Reset());
        Assert.Equal("bottom", Dock.Bottom2.Fold());
        Assert.Equal("right", Dock.Right.Fold());
    }

    [Fact]
    public void SizesAreKeptRoundedAndPutBackWhenOutOfRange()
    {
        var saved = new SavedLayout();
        saved.Resize("left", 301.4);
        saved.Resize("split", 33.33);
        saved.Resize("bottom", -1);
        saved.Resize("nothing", 5);
        Assert.Equal(301, saved.LeftWidth);
        Assert.Equal(33.3, saved.BottomSplitPercent);
        Assert.Equal(SavedLayout.DefaultBottomHeight, saved.BottomHeight);

        var odd = new SavedLayout { Left = null, LeftWidth = 120, RightWidth = 500, BottomHeight = 50, BottomSplitPercent = 90 }.Normalize();
        Assert.Empty(odd.Left);
        Assert.Equal(SavedLayout.DefaultLeftWidth, odd.LeftWidth);
        Assert.Equal(500, odd.RightWidth);
        Assert.Equal(SavedLayout.DefaultBottomHeight, odd.BottomHeight);
        Assert.Equal(SavedLayout.DefaultBottomSplit, odd.BottomSplitPercent);
    }

    [Fact]
    public async Task TheLayoutIsKeptInTheBrowserAsBefore()
    {
        const string kept = "{\"Left\":[\"clock\"],\"Right\":[],\"Bottom\":[\"memory\"],\"BottomRight\":[],\"BottomSplit\":true,\"BottomSplitPercent\":40,\"LeftWidth\":300,\"RightWidth\":360,\"BottomHeight\":260}";
        var js = new RecordingJs { Answer = (identifier, args) => identifier == "exuarchStore.get" && (string)args[0] == "exuarch.runLayout" ? kept : null };
        var store = new RunLayoutStore(new BrowserStore(js));
        var layout = await store.LoadLayout();
        Assert.Equal(new[] { "clock" }, layout.Left);
        Assert.True(layout.BottomSplit);
        Assert.Equal(40, layout.BottomSplitPercent);
        await store.SaveLayout(layout);
        Assert.Equal(("exuarchStore.set", "exuarch.runLayout", kept), (js.Calls[^1].Identifier, (string)js.Calls[^1].Args[0], (string)js.Calls[^1].Args[1]));
        Assert.Null(await new RunLayoutStore(new BrowserStore(new RecordingJs())).LoadLayout());
        var broken = new RecordingJs { Answer = (_, _) => "{ not json" };
        Assert.Equal(SavedLayout.DefaultLeftWidth, (await new RunLayoutStore(new BrowserStore(broken)).LoadLayout()).LeftWidth);
    }
}

public class RunSessionTests
{
    private sealed class Host : IRunHost
    {
        public readonly ManualTime Time = new ManualTime();
        public RunSession Session;
        public int Redraws, Screens;
        // Pauses the run once this much time has passed.
        public double PauseAfter = double.MaxValue;

        public void Redraw() => Redraws++;
        public void ShowScreens() => Screens++;
        public Task NextTurn() => Wait(1);
        public Task Delay(int milliseconds) => Wait(milliseconds);

        private Task Wait(double milliseconds)
        {
            Time.Advance(milliseconds);
            if (Time.GetTimestamp() >= PauseAfter * 1000 && Session.Running) return Session.ToggleRun(this);
            return Task.CompletedTask;
        }
    }

    private static (RunSession Session, Host Host) Open(string package = "BYOC-16")
    {
        var host = new Host();
        var session = new RunSession(new Analytics(new RecordingJs()), DeviceRegistry.CreateDefault(), () => Task.CompletedTask, host.Time);
        host.Session = session;
        var built = BuiltInPackages.Get(package);
        Assert.True(session.Show(new Machine(built.Machine, built.Programs[0].Source)));
        return (session, host);
    }

    [Fact]
    public void ATickAndAnInstructionWorkOnlyWhilePaused()
    {
        var (session, _) = Open();
        Assert.Equal(RunState.Paused, session.State);
        Assert.True(session.CanRun);
        Assert.True(session.CanStep);
        Assert.False(session.Show(session.Machine));
        session.TickOnce();
        Assert.Equal(1, session.Machine.Cycles);
        session.StepInstruction();
        Assert.NotNull(session.Machine.LastTick.FetchedFromAddress);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public async Task ARunStopsAtABreakpoint()
    {
        var (first, _) = Open();
        first.StepInstruction();
        first.StepInstruction();
        int second = first.Machine.LastTick.FetchedFromAddress.Value;
        var (session, host) = Open();
        session.ToggleBreakpoint(second);
        var speed = RunSpeed.Session;
        var (slider, max) = (speed.Slider, speed.Max);
        try
        {
            speed.Max = true;
            await session.ToggleRun(host);
            Assert.Equal(second, session.Machine.LastTick.FetchedFromAddress);
            Assert.False(session.Running);
            Assert.Equal(RunState.Paused, session.State);
            Assert.True(host.Redraws > 0);
        }
        finally
        {
            (speed.Slider, speed.Max) = (slider, max);
        }
    }

    [Fact]
    public async Task ARunAtASetSpeedKeepsTheClockRate()
    {
        var (session, host) = Open();
        var speed = RunSpeed.Session;
        var (slider, max) = (speed.Slider, speed.Max);
        try
        {
            (speed.Slider, speed.Max) = (201, false);
            Assert.Equal(16, speed.Hz);
            host.PauseAfter = 1000;
            await session.ToggleRun(host);
            Assert.False(session.Running);
            Assert.InRange(session.Machine.Cycles, 15, 17);
            Assert.InRange(session.Meter.Samples.Count, 4, 5);
            Assert.InRange(session.Meter.Current, 10, 22);
            Assert.True(host.Redraws > 50);
        }
        finally
        {
            (speed.Slider, speed.Max) = (slider, max);
        }
    }

    [Fact]
    public void AnExpandedBusShowsItsSixteenWiresLitByTheValueOnIt()
    {
        var (session, _) = Open();
        var layout = new SchematicLayout(session.Machine.Definition, DeviceRegistry.CreateDefault());
        Assert.Empty(new LiveWiring(session, layout).Wires);

        session.ToggleBus("main");
        session.TickOnce();
        var transfer = session.Last.TransferOn("main");
        Assert.NotNull(transfer);
        var wiring = new LiveWiring(session, layout);
        Assert.DoesNotContain(wiring.Buses, b => b.Id == "main");
        var wires = Assert.Single(wiring.Wires);
        Assert.Equal("main", wires.Id);
        Assert.True(wires.Driven);
        Assert.Equal(16, wires.Wires.Count);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => 15 - i), wires.Wires.Select(w => w.Bit));
        Assert.Equal(15 * LiveWiring.WireSpacing, wires.Wires[15].Y - wires.Wires[0].Y);
        Assert.Equal(layout.BusY("main"), (wires.Wires[0].Y + wires.Wires[15].Y) / 2);
        foreach (var wire in wires.Wires) Assert.Equal(((transfer.Value >> wire.Bit) & 1) == 1, wire.Lit);

        session.ToggleBus("main");
        Assert.Empty(new LiveWiring(session, layout).Wires);
        Assert.Contains(new LiveWiring(session, layout).Buses, b => b.Id == "main");
    }
}
