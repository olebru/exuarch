using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Exuarch.Core;

namespace Exuarch.Core.Tests.Snapshots;

// What every built in program does tick by tick, where every machine's cards and wires are drawn, what the editing
// operations make of every machine, and the reference pages.
public class MachineSnapshotTests
{
    public static TheoryData<string> Packages() => ToolchainSnapshotTests.Packages();

    private sealed class ManualTime : TimeProvider
    {
        public long Milliseconds;
        public override long GetTimestamp() => Milliseconds;
        public override long TimestampFrequency => 1000;
    }

    // A real time clock reads a clock moved by hand, a millisecond every 250 ticks, and the keypad is played the same
    // way every run, so a program behaves the same each time.
    private sealed class Run
    {
        private readonly ManualTime time = new ManualTime();
        public readonly Machine Machine;
        private readonly Keypad keypad;
        private long ticks;

        public Run(MachinePackage package, string source, bool recordHistory)
        {
            Machine = new Machine(package.Machine, source, DeviceRegistry.CreateDefault(time)) { RecordHistory = recordHistory };
            keypad = Machine.Devices.OfType<Keypad>().FirstOrDefault();
        }

        public void Tick()
        {
            Machine.SingleStep();
            ticks++;
            if (ticks % 250 == 0) time.Milliseconds++;
            if (keypad == null) return;
            switch (ticks % 6000)
            {
                case 400: keypad.Press(Keypad.Keys.Space); break;
                case 900: keypad.Release(Keypad.Keys.Space); break;
                case 1500: keypad.Press(Keypad.Keys.Left); break;
                case 2600: keypad.Release(Keypad.Keys.Left); break;
                case 3000: keypad.Press(Keypad.Keys.Up); break;
                case 3200: keypad.Release(Keypad.Keys.Up); break;
                case 3800: keypad.Press(Keypad.Keys.Right); break;
                case 4700: keypad.ReleaseAll(); break;
                case 5200: keypad.Press(Keypad.Keys.Down); break;
                case 5900: keypad.Release(Keypad.Keys.Down); break;
            }
        }
    }

    private const int TracedTicks = 3000;
    private const int LongTicks = 150_000;

    [Theory]
    [MemberData(nameof(Packages))]
    public void Execution(string name)
    {
        var package = BuiltInPackages.Get(name);
        var text = new StringBuilder();
        foreach (var program in package.Programs)
        {
            text.Append($"== {program.Name}\n");

            // Every tick recorded, as the Run view shows it.
            var traced = new Run(package, program.Source, true);
            text.Append($"  warnings: {string.Join(" / ", traced.Machine.MicrocodeWarnings.Select(w => $"{w.Severity} {w.Instruction} {w.Step} {w.Signal} {w.Message}"))}\n");
            using (var digest = new Snapshot.Digest(25))
            {
                for (int i = 0; i < TracedTicks && !traced.Machine.IsHalted; i++)
                {
                    traced.Tick();
                    digest.Add(Describe(traced.Machine.LastTick) + $" | next {Describe(traced.Machine)}");
                }
                text.Append(digest.Finish("trace"));
            }
            text.Append($"  history {traced.Machine.History.Count}, first cycle {traced.Machine.History.FirstOrDefault()?.Cycle}\n");
            text.Append(State(traced.Machine));

            // A long run with nothing recorded, as at full speed.
            var fast = new Run(package, program.Source, false);
            for (int i = 0; i < LongTicks && !fast.Machine.IsHalted; i++) fast.Tick();
            text.Append($"  after {fast.Machine.Cycles} fast ticks, last tick {(fast.Machine.LastTick == null ? "none" : Describe(fast.Machine.LastTick))}\n");
            text.Append(State(fast.Machine));

            // Instruction steps, as the Instruction button does them.
            var stepped = new Run(package, program.Source, true);
            var counts = new List<int>();
            for (int i = 0; i < 40 && !stepped.Machine.IsHalted; i++) counts.Add(stepped.Machine.StepInstruction());
            text.Append($"  instruction steps {string.Join(",", counts)} at {stepped.Machine.CurrentInstructionAddress}\n");
        }
        Snapshot.Match($"execution-{name}", text.ToString());
    }

    private static string Describe(Machine machine)
    {
        var next = machine.NextStep;
        return $"pc? {machine.CurrentInstructionAddress} st {machine.Status:X} ds {machine.DecoderStatus:X} nds {machine.NextDecoderStatus:X} ms {machine.MicroStepRegister} halt {machine.IsHalted} "
               + $"{next?.Instruction.Mnemonic}/{next?.Offset} code {string.Join(",", machine.CurrentMicroCode?.Select(m => m.DeviceID + "." + m.Function) ?? Array.Empty<string>())}";
    }

    private static string Describe(TickRecord tick)
    {
        var text = new StringBuilder($"c{tick.Cycle} {tick.Instruction}#{tick.StepIndex} s{tick.Status:X} m{tick.MicroStep} r{tick.RomAddress:X} f{tick.FetchedFromAddress} [{string.Join(",", tick.Signals)}]");
        foreach (var t in tick.Transfers) text.Append($" {t.Bus}:{t.Driver}={t.Value:X}>{string.Join("+", t.Readers)}");
        foreach (var c in tick.Changes) text.Append($" {c.Device}:{c.Before:X}->{c.After:X}");
        foreach (var w in tick.Writes) text.Append($" {w.Device}[{w.Bank}]{w.Address:X}={w.Value:X}");
        return text.ToString();
    }

    // Everything a program can leave behind, read through each device's public members.
    private static string State(Machine machine)
    {
        var text = new StringBuilder($"  state cycles {machine.Cycles} status {machine.Status:X} halted {machine.IsHalted}\n");
        foreach (var device in machine.Devices)
        {
            text.Append($"    {device.ID()} {device.GetType().Name} out {device.IsOutputEnabled()} lines {string.Join(",", device.SignalLines())}: {DeviceState(device)}\n");
        }
        return text.ToString();
    }

    private static string DeviceState(IBusDevice device)
    {
        return device switch
        {
            MMU mmu => $"cs {mmu.ChipSelectRegister.Data} banks {string.Join(",", mmu.RamBanks.Select(b => ToolchainSnapshotTests.Hash(string.Join(",", b.memory)) + "@" + b.memoryAddress + "/" + b.WriteCount + "/" + b.LastWriteAddress))}",
            RamModule ram => $"mar {ram.memoryAddress} writes {ram.WriteCount} last {ram.LastWriteAddress} mem {ToolchainSnapshotTests.Hash(string.Join(",", ram.memory))}",
            MemoryModule memory => $"mar {memory.memoryAddress} mem {ToolchainSnapshotTests.Hash(string.Join(",", memory.memory))}",
            DoubleFramebuffer pages => $"front {pages.FrontBuffer} x {pages.X} y {pages.Y} writes {pages.WriteCount} last {pages.LastWriteAddress}={pages.LastWriteValue} shown {ToolchainSnapshotTests.Hash(string.Join(",", pages.Shown))} back {ToolchainSnapshotTests.Hash(string.Join(",", pages.Pixels))}",
            Framebuffer screen => $"x {screen.X} y {screen.Y} writes {screen.WriteCount} last {screen.LastWriteAddress}={screen.LastWriteValue} pixels {ToolchainSnapshotTests.Hash(string.Join(",", screen.Pixels))}",
            DepthBuffer depth => $"x {depth.X} y {depth.Y} writes {depth.WriteCount} depths {ToolchainSnapshotTests.Hash(string.Join(",", depth.Depths))}",
            CharacterDisplay lcd => $"cursor {lcd.Cursor} writes {lcd.WriteCount} text {ToolchainSnapshotTests.Flat(lcd.Text)}",
            RegisterFile file => $"select {file.Selected} values {string.Join(",", file.Values)}",
            Blitter b => $"{b.X},{b.Y} {b.Width}x{b.Height} colour {b.Colour} busy {b.Busy} row {b.Row} col {b.Column} pixels {b.PixelsDrawn} jobs {b.JobsDone}",
            Rasterizer r => $"list {r.ListAddress} count {r.Count} busy {r.Busy} triangle {r.Triangle} stage {r.Stage} drawn {r.TrianglesDrawn} pixels {r.PixelsDrawn} hidden {r.PixelsHidden} jobs {r.JobsDone}",
            MultiplyAccumulate mac => $"a {mac.A} b {mac.B} acc {mac.Accumulator} result {mac.Result}",
            TickTimer timer => $"period {timer.Period} count {timer.Count} running {timer.Running} expired {timer.Expired}",
            RealTimeClock rtc => $"interval {rtc.Interval} running {rtc.Running} expired {rtc.Expired}",
            InterruptController ic => $"enabled {ic.Enabled} mask {ic.Mask} pending {ic.Pending} requests {ic.Requests}",
            Keypad keypad => $"data {keypad.Data} held {keypad.Held}",
            InstructionRegister ir => $"data {ir.Data}",
            DualPortRegister dual => $"data {dual.Data}",
            Register register => $"data {register.Data}",
            _ => "",
        };
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Layout(string name)
    {
        var package = BuiltInPackages.Get(name);
        var registry = DeviceRegistry.CreateDefault();
        var text = new StringBuilder();
        var shipped = package.Machine.Clone();
        shipped.EnsureLayout();
        var auto = package.Machine.Clone();
        auto.EnsureLayout(force: true);
        var bare = package.Machine.Clone();
        foreach (var bus in bare.Buses) bus.Layout = null;
        foreach (var device in bare.Devices) device.Layout = null;
        bare.Decoder.Layout = null;
        bare.EnsureLayout();
        var half = package.Machine.Clone();
        foreach (var device in half.Devices.Where((d, i) => i % 2 == 0)) device.Layout = null;
        half.EnsureLayout();
        foreach (var (label, machine) in new[] { ("shipped", shipped), ("auto", auto), ("no layout", bare), ("half layout", half) })
        {
            text.Append($"== {label}\n").Append(DescribeLayout(machine, registry));
            var draft = new SchematicLayout(machine, registry) { Draft = true };
            using var paths = new Snapshot.Digest(0);
            foreach (var device in machine.Devices)
            {
                var connections = draft.Info(device).Connections;
                for (int i = 0; i < connections.Count; i++)
                {
                    var target = device.Connections.TryGetValue(connections[i].Name, out var id) ? machine.FindDevice(id) : null;
                    if (target != null) paths.Add(draft.ConnectionPath(device, i, target));
                }
            }
            text.Append(paths.Finish("  draft paths"));
        }
        foreach (var type in registry.TypeInfos.Select(t => t.Type))
        {
            var spot = SchematicPlacement.FreeSpot(shipped, new DeviceDefinition { Id = "new", Type = type });
            text.Append($"  free spot for {type}: {spot.X},{spot.Y}\n");
        }
        Snapshot.Match($"layout-{name}", text.ToString());
    }

    private static string DescribeLayout(MachineDefinition machine, DeviceRegistry registry)
    {
        var layout = new SchematicLayout(machine, registry);
        var text = new StringBuilder($"  canvas {layout.CanvasWidth}x{layout.CanvasHeight} content {layout.ContentWidth}\n");
        foreach (var bus in machine.Buses) text.Append($"  bus {bus.Id} {bus.Layout?.X},{bus.Layout?.Y}\n");
        text.Append($"  decoder {machine.Decoder?.Layout?.X},{machine.Decoder?.Layout?.Y}\n");
        foreach (var card in layout.Cards()) text.Append($"  card {card.Id} {card.X},{card.Y} {card.Width}x{card.Height}\n");
        foreach (var device in machine.Devices)
        {
            foreach (var port in device.Ports())
            {
                var anchor = layout.PortAnchor(device, port.Key);
                text.Append($"  port {device.Id}.{port.Key} {anchor.X},{anchor.Y} {(anchor.Bottom ? "bottom" : "top")}\n");
            }
            var connections = layout.Info(device).Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                if (!device.Connections.TryGetValue(connections[i].Name, out var targetId)) continue;
                var target = machine.FindDevice(targetId);
                text.Append($"  wire {device.Id}.{connections[i].Name} {Points(layout.RoutePoints("device", device.Id, i))} path {(target == null ? "-" : layout.ConnectionPath(device, i, target))}\n");
            }
        }
        for (int i = 0; i < SchematicLayout.DecoderSockets.Length; i++)
        {
            var socket = SchematicLayout.DecoderSockets[i].Name;
            var target = layout.DecoderTarget(socket);
            var device = target == null ? null : machine.FindDevice(target);
            text.Append($"  decoder.{socket} -> {target} {(target == null ? "" : Points(layout.RoutePoints("decoder", null, i)))} path {(device == null ? "-" : layout.DecoderPath(i, device))}\n");
        }
        return text.ToString();
    }

    private static string Points(IReadOnlyList<(double X, double Y)> points)
    {
        return points == null ? "unrouted" : string.Join(" ", points.Select(p => $"{p.X},{p.Y}"));
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Editing(string name)
    {
        var package = BuiltInPackages.Get(name);
        var text = new StringBuilder();
        foreach (var device in package.Machine.Devices)
        {
            var usages = package.Machine.SignalUsages(device.Id);
            text.Append($"  {device.Id} used {usages.Count}: {string.Join(" ", usages.Take(6).Select(u => $"{u.Instruction.Mnemonic}/{u.Step}/{u.Signal}"))}\n");
            var renamed = package.Machine.Clone();
            renamed.RenameDevice(device.Id, device.Id + "_x");
            text.Append($"    rename {ToolchainSnapshotTests.Hash(renamed.ToJson())}\n");
            var stripped = package.Machine.Clone();
            text.Append($"    remove signals {stripped.RemoveSignalsOf(device.Id)} {ToolchainSnapshotTests.Hash(stripped.ToJson())}\n");
            var removed = package.Machine.Clone();
            removed.RemoveDevice(device.Id);
            text.Append($"    remove {ToolchainSnapshotTests.Hash(removed.ToJson())} errors {Machine.ValidateDefinition(removed).Count}\n");
        }
        foreach (var bus in package.Machine.Buses)
        {
            var renamed = package.Machine.Clone();
            renamed.RenameBus(bus.Id, bus.Id + "_x");
            var removed = package.Machine.Clone();
            removed.RemoveBus(bus.Id);
            text.Append($"  bus {bus.Id} rename {ToolchainSnapshotTests.Hash(renamed.ToJson())} remove {ToolchainSnapshotTests.Hash(removed.ToJson())}\n");
        }
        text.Append($"  next ids {package.Machine.NextFreeId("r")} {package.Machine.NextFreeId("mem")} {package.Machine.NextFreeId("bus")}\n");
        Snapshot.Match($"editing-{name}", text.ToString());
    }

    private static string Hit(HandbookHit h) => $"{h.Kind}/{h.Target}/{h.Title}/{h.Section}/{h.Score}/{ToolchainSnapshotTests.Hash(h.Snippet)}";

    private static string Try(Func<string> make)
    {
        try { return make(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    [Fact]
    public void Reference()
    {
        var text = new StringBuilder();
        foreach (var group in DeviceReference.ByCategory)
        {
            text.Append($"== {group.Key}\n");
            foreach (var info in group) text.Append($"  {info.Type}: {DeviceReference.Summary(info)} | {ToolchainSnapshotTests.Hash(DeviceReference.Markdown(info.Type))}\n");
        }
        text.Append($"  exists nosuch {DeviceReference.Exists("nosuch")} markdown nosuch {Try(() => ToolchainSnapshotTests.Hash(DeviceReference.Markdown("nosuch")))}\n");
        var hrefs = new List<string> { "exuarch:nosuch/x", "exuarch:", "exuarch:device/", "exuarch:tab/Nowhere", "exuarch:guide/nosuch", "exuarch:reference/nosuch", "https://example.com", "exuarch:package/NOSUCH" };
        foreach (var package in BuiltInPackages.All)
        {
            var links = ReadmeLinks.In(package.Readme ?? "").ToList();
            text.Append($"== {package.Name}: {links.Count} links\n");
            foreach (var href in links.Concat(hrefs).Concat(new[] { "exuarch:device/nosuch", "exuarch:instruction/NOSUCH", "exuarch:program/No such" }))
            {
                var parsed = ReadmeLinks.TryParse(href, out var kind, out var target);
                text.Append($"  {href} parsed {parsed} {kind}/{target} problem {ReadmeLinks.Problem(package, href)}\n");
            }
        }
        foreach (var query in new[] { "alu", "interrupt", "MOV", "bus", "blitter", "", "zzzz", "register file" })
        {
            text.Append($"  search '{query}': {string.Join(" | ", Handbook.Search(query).Select(Hit))}\n");
            text.Append($"  search '{query}' in RISC-16: {string.Join(" | ", Handbook.Search(query, BuiltInPackages.Get("RISC-16")).Select(Hit))}\n");
        }
        Snapshot.Match("reference", text.ToString());
    }
}
