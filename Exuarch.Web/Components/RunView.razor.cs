using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Exuarch.Web.Components
{
    public partial class RunView : IDisposable
    {
        private const int TraceRows = 120;

        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private Analytics Analytics { get; set; }

        [Parameter] public Machine Machine { get; set; }
        [Parameter] public DeviceRegistry Registry { get; set; } = DeviceRegistry.CreateDefault();
        [Parameter] public EventCallback OnRestart { get; set; }
        // Shown in the top bar: which machine this is and which program is loaded.
        [Parameter] public string MachineName { get; set; }
        [Parameter] public string ProgramName { get; set; }

        private Machine shown;
        private bool running;
        private string runtimeError;
        // Speed settings last for the session, so they survive switching tabs.
        // The speed slider's position, 0 to SliderSteps; it starts at 16 Hz.
        private static int speedSlider = 201;
        // Ignore the slider and run as many ticks as the browser allows.
        private static bool maxSpeed;
        // Measured clock speed while running: (seconds of running time, ticks per second).
        private readonly List<(double Seconds, double Hz)> speedSamples = new List<(double, double)>();
        private double runningSeconds;
        private const double SampleSeconds = 0.25;
        private const double SpeedWindowSeconds = 60;
        private readonly HashSet<int> breakpoints = new HashSet<int>();
        private string bottomTab = "memory";
        // The tab on show in the second group below, when the panels below are split.
        private string bottomTab2;
        private string memoryDevice;
        private int memoryBank;
        private int? lastScrolledAddress;
        private double zoom = 1;
        private bool fitPending = true;
        private ElementReference schematicElement;
        private ElementReference runElement;
        private bool stopRequested;
        private bool wholeRom;
        // Panels the viewer has folded away, kept in the browser: "left", "right", "lines", "bottom", and any panel by its id:
        // "clock", "now", "program", "memory", "decoder", "trace" and "device:<id>" for a screen, LCD or keypad.
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private bool panelsLoaded;
        private const string PanelsKey = "exuarch.runPanels";

        protected override void OnParametersSet()
        {
            if (!ReferenceEquals(shown, Machine))
            {
                shown = Machine;
                stopRequested = true;
                running = false;
                runtimeError = null;
                lastScrolledAddress = null;
                fitPending = true;
                speedSamples.Clear();
                runningSeconds = 0;
                if (Machine != null && (memoryDevice == null || Machine.Device(memoryDevice) == null))
                {
                    memoryDevice = Machine.Definition.ProgramMemory ?? Machine.Devices.FirstOrDefault(d => d is MemoryModule || d is MMU)?.ID();
                }
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!panelsLoaded)
            {
                panelsLoaded = true;
                await LoadLayout();
                var saved = await JS.InvokeAsync<string>("exuarchStore.get", PanelsKey);
                if (!string.IsNullOrEmpty(saved))
                {
                    collapsed.UnionWith(saved.Split(',', StringSplitOptions.RemoveEmptyEntries));
                    fitPending = true;
                    StateHasChanged();
                }
            }
            // Once per Run view element (it is made again when the machine goes away and comes back).
            if (Machine != null && runElement.Context != null && guardedElement != runElement.Id)
            {
                guardedElement = runElement.Id;
                await JS.InvokeVoidAsync("exuarchKeys.runView", runElement);
                selfReference ??= DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("exuarchRunLayout.attach", runElement, selfReference);
                await JS.InvokeVoidAsync("exuarchRunLayout.observe", schematicElement, selfReference);
            }
            if (fitPending && Machine != null)
            {
                fitPending = false;
                await Fit();
            }
            var address = Machine?.CurrentInstructionAddress;
            if (address != null && address != lastScrolledAddress)
            {
                lastScrolledAddress = address;
                await JS.InvokeVoidAsync("exuarchEditor.scrollToId", $"listing-{address}");
            }
        }

        public void Dispose()
        {
            stopRequested = true;
            selfReference?.Dispose();
        }

        private bool Collapsed(string panel) => collapsed.Contains(panel);

        private async Task TogglePanel(string panel)
        {
            if (!collapsed.Remove(panel)) collapsed.Add(panel);
            // The listing needs scrolling to the current line again; a change of width is left to SchematicResized.
            lastScrolledAddress = null;
            await JS.InvokeAsync<bool>("exuarchStore.set", PanelsKey, string.Join(",", collapsed.OrderBy(p => p)));
        }

        // A bottom tab opens the bottom panel if it was folded away.
        private async Task ShowBottom(string dock, string tab)
        {
            if (dock == "bottom2") bottomTab2 = tab; else bottomTab = tab;
            if (Collapsed("bottom")) await TogglePanel("bottom");
        }

        // The drawing is shown at its actual size, 100%, and only zoomed out as far as it takes to fit a machine that is
        // wider than its panel; it is never zoomed in by itself. This holds whenever the panel changes width, through the
        // window or the side column's handle, until the viewer zooms by hand. Clicking the percentage goes back to it.
        private bool autoZoom = true;
        private double schematicWidth;

        private double FitZoom(double width) => Math.Clamp(Math.Floor((width - 24) / Layout.ContentWidth * 100) / 100, 0.4, 1.0);

        private async Task Fit()
        {
            autoZoom = true;
            var rect = await JS.InvokeAsync<ElementRect>("exuarchEditor.rect", schematicElement);
            if (rect.Width <= 0) return;
            schematicWidth = rect.Width;
            zoom = FitZoom(rect.Width);
            StateHasChanged();
        }

        [JSInvokable]
        public void SchematicResized(double width)
        {
            if (width <= 0 || Math.Abs(width - schematicWidth) < 1) return;
            schematicWidth = width;
            if (!autoZoom) return;
            var fitted = FitZoom(width);
            if (fitted == zoom) return;
            zoom = fitted;
            StateHasChanged();
        }

        private void Zoom(double factor)
        {
            autoZoom = false;
            zoom = Math.Clamp(Math.Round(zoom * factor, 2), 0.4, 1.6);
        }

        // ---- Controls ----

        // The slider runs from 1 Hz to SliderMaxHz on a log scale, so each step is the same factor faster and the
        // speeds from 1 Hz to 1 kHz have as much room on it as those from 1 kHz to 1 MHz. Speeds are rounded to two
        // significant figures, so they read as 140 Hz or 12 kHz.
        private const int SliderSteps = 1000;
        private const double SliderMaxHz = 1_000_000;
        private int Hz
        {
            get
            {
                double hz = Math.Pow(SliderMaxHz, speedSlider / (double)SliderSteps);
                if (hz < 100) return (int)Math.Round(hz);
                double unit = Math.Pow(10, Math.Floor(Math.Log10(hz)) - 1);
                return (int)(Math.Round(hz / unit) * unit);
            }
        }
        // A speed in a few characters for the slider: 16 Hz, 1.2 kHz, 500 kHz, 1 MHz.
        private static string ShortHz(int hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000.0:0.#} MHz";
            return hz < 1000 ? $"{hz} Hz" : $"{hz / 1000.0:0.#} kHz";
        }
        private string State
        {
            get
            {
                if (runtimeError != null) return "error";
                if (Machine.IsHalted) return "halted";
                return running ? "running" : "paused";
            }
        }

        private bool Tick()
        {
            try
            {
                Machine.SingleStep();
                if (Machine.IsHalted)
                {
                    // However it got there, Run, Tick or Instruction, the program reached HLT.
                    _ = Analytics.Halted();
                    return false;
                }
                if (Machine.LastTick?.FetchedFromAddress is int address && breakpoints.Contains(address)) return false;
                return true;
            }
            catch (Exception e)
            {
                runtimeError = $"Cycle {Machine.Cycles + 1}: {e.Message}";
                _ = Analytics.RuntimeError(e.Message);
                return false;
            }
        }
        private void TickOnce()
        {
            if (running || Machine.IsHalted || runtimeError != null) return;
            Tick();
        }
        private void StepInstruction()
        {
            if (running || Machine.IsHalted || runtimeError != null) return;
            for (int i = 0; i < 10000; i++)
            {
                if (!Tick() || Machine.LastTick.FetchedFromAddress != null) break;
            }
        }
        // Runs until paused, halted or a breakpoint. At a set speed the ticks due are worked out from the time that
        // has passed, so the clock keeps its rate however long a frame takes to draw; at max speed each frame runs
        // as many ticks as fit in its time budget, then draws.
        private async Task ToggleRun()
        {
            if (running) { stopRequested = true; return; }
            if (Machine.IsHalted || runtimeError != null) return;
            running = true;
            stopRequested = false;
            var machine = Machine;
            var clock = Stopwatch.StartNew();
            long startCycles = Machine.Cycles;
            bool startedAtMax = maxSpeed;
            var frame = new Stopwatch();
            // At max speed a frame runs ticks for MaxSpeedFrameMilliseconds and then hands the browser its turn, so
            // clicks and keys are answered soon. The whole view is drawn only every budget milliseconds: drawing it
            // takes the same time however long the frames ran, so the budget grows to keep it to about a tenth of the
            // time: the view updates less often, but the machine runs faster.
            var between = new Stopwatch();
            double budget = MaxSpeedFrameMilliseconds;
            // A double buffered screen shows a finished frame when it swaps. At max speed the run stops there to show
            // it, sending only the screen's changes, and draws the rest of the view on its own slower beat; otherwise
            // the screen would only show the frames that happened to be in front when the view was drawn.
            var doubles = Machine.Devices.OfType<DoubleFramebuffer>().ToArray();
            long Swaps() { long swaps = 0; foreach (var screen in doubles) swaps += screen.Swaps; return swaps; }
            var sinceView = Stopwatch.StartNew();
            bool viewDrawn = false;
            double owed = 0, lastFrameAt = 0, sampleAt = 0;
            int sampleCycles = Machine.Cycles;
            while (!stopRequested && ReferenceEquals(machine, Machine))
            {
                if (viewDrawn) budget = Math.Clamp(between.Elapsed.TotalMilliseconds * OverheadShare, MaxSpeedFrameMilliseconds, MaxSpeedLongestFrameMilliseconds);
                frame.Restart();
                double now = clock.Elapsed.TotalSeconds;
                bool keepGoing = true;
                // Fast runs skip the per tick detail and record only the last tick of the frame, the one on screen.
                bool recordEvery = !maxSpeed && Hz <= FullRecordingHz;
                Machine.RecordHistory = recordEvery;
                bool swapped = false;
                if (maxSpeed)
                {
                    long swaps = Swaps();
                    while (keepGoing && !swapped && frame.Elapsed.TotalMilliseconds < MaxSpeedFrameMilliseconds)
                    {
                        for (int i = 0; i < 256 && keepGoing; i++) keepGoing = Tick();
                        swapped = doubles.Length > 0 && Swaps() != swaps;
                    }
                    owed = 0;
                }
                else
                {
                    owed = Math.Min(owed + Hz * (now - lastFrameAt), Hz * 0.25 + 1);
                    while (keepGoing && owed >= (recordEvery ? 1 : 2) && frame.ElapsedMilliseconds < MaxSpeedFrameMilliseconds)
                    {
                        keepGoing = Tick();
                        owed--;
                    }
                }
                Machine.RecordHistory = true;
                if (keepGoing && !recordEvery && (maxSpeed || owed >= 1))
                {
                    keepGoing = Tick();
                    owed = Math.Max(0, owed - 1);
                }
                lastFrameAt = now;
                if (now - sampleAt >= SampleSeconds)
                {
                    AddSpeedSample((Machine.Cycles - sampleCycles) / (now - sampleAt), now - sampleAt);
                    sampleAt = now;
                    sampleCycles = Machine.Cycles;
                }
                viewDrawn = !keepGoing || !maxSpeed || sinceView.Elapsed.TotalMilliseconds >= budget;
                if (viewDrawn)
                {
                    between.Restart();
                    StateHasChanged();
                    sinceView.Restart();
                }
                else if (swapped)
                {
                    FramebufferView.ShowChanges();
                }
                if (!keepGoing) break;
                if (maxSpeed)
                {
                    await ScreenInterop.NextTurn();
                }
                else
                {
                    await Task.Delay(Math.Max(1, Math.Min(16, (int)(1000 / Math.Max(1, Hz)) - (int)frame.ElapsedMilliseconds)));
                }
            }
            if (ReferenceEquals(machine, Machine))
            {
                running = false;
                double elapsed = clock.Elapsed.TotalSeconds - sampleAt;
                if (elapsed > 0.05) AddSpeedSample((Machine.Cycles - sampleCycles) / elapsed, elapsed);
                // How long it ran, and how fast the simulator runs flat out when it ran at max speed throughout.
                double total = clock.Elapsed.TotalSeconds;
                _ = Analytics.Ran(startedAtMax && maxSpeed, total, total > 0 ? (Machine.Cycles - startCycles) / total : 0);
            }
            StateHasChanged();
        }
        // How long a max speed frame runs before the browser has its turn, which is also how long a click can wait.
        private const int MaxSpeedFrameMilliseconds = 25;
        // The longest the whole view goes undrawn at max speed.
        private const int MaxSpeedLongestFrameMilliseconds = 250;
        // The view is drawn after this many times as long as drawing it took, about a tenth of the time.
        private const double OverheadShare = 9;
        // At or below this speed every tick is recorded, so the trace is complete.
        private const int FullRecordingHz = 200;

        private void AddSpeedSample(double hz, double seconds)
        {
            runningSeconds += seconds;
            speedSamples.Add((runningSeconds, hz));
            while (speedSamples.Count > 0 && speedSamples[0].Seconds < runningSeconds - SpeedWindowSeconds) speedSamples.RemoveAt(0);
        }
        private double CurrentHz { get { return speedSamples.Count == 0 ? 0 : speedSamples[^1].Hz; } }
        private double PeakHz { get { return speedSamples.Count == 0 ? 0 : speedSamples.Max(s => s.Hz); } }
        private static string FormatHz(double hz)
        {
            if (hz >= 1_000_000) return $"{hz / 1_000_000:0.00} MHz";
            if (hz >= 1_000) return $"{hz / 1_000:0.00} kHz";
            return $"{hz:0.0} Hz";
        }
        // The chart's top value: a round number above the highest sample and the target.
        private double ChartMax
        {
            get
            {
                double top = Math.Max(PeakHz, maxSpeed ? 0 : Hz) * 1.1;
                if (top <= 0) return 10;
                double magnitude = Math.Pow(10, Math.Floor(Math.Log10(top)));
                foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
                {
                    if (step * magnitude >= top) return step * magnitude;
                }
                return 10 * magnitude;
            }
        }
        private const double ChartWidth = 360, ChartHeight = 70;
        private string SpeedPoints(bool area)
        {
            if (speedSamples.Count == 0) return "";
            double end = speedSamples[^1].Seconds, span = ChartSpan, start = end - span, top = ChartMax;
            string Point(double seconds, double hz) => FormattableString.Invariant($"{(seconds - start) / span * ChartWidth:0.#},{ChartHeight - Math.Min(1, hz / top) * ChartHeight:0.#}");
            var points = string.Join(" ", speedSamples.Select(s => Point(s.Seconds, s.Hz)));
            return area ? $"{Point(speedSamples[0].Seconds, 0)} {points} {Point(end, 0)}" : points;
        }
        // Seconds across the chart: grows with the run until it reaches the window.
        private double ChartSpan { get { return Math.Min(SpeedWindowSeconds, Math.Max(5, runningSeconds)); } }
        private double TargetY { get { return ChartHeight - Math.Min(1, Hz / ChartMax) * ChartHeight; } }
        private async Task Restart()
        {
            stopRequested = true;
            running = false;
            await OnRestart.InvokeAsync();
        }
        // While a keypad has the keyboard, arrows and space go to it and Esc gives the keyboard back.
        private Keypad capturedKeypad;
        private static Keypad.Keys? KeypadKey(string key)
        {
            return key switch
            {
                "ArrowUp" => Keypad.Keys.Up,
                "ArrowDown" => Keypad.Keys.Down,
                "ArrowLeft" => Keypad.Keys.Left,
                "ArrowRight" => Keypad.Keys.Right,
                " " => Keypad.Keys.Space,
                _ => null,
            };
        }
        private string guardedElement;
        private async Task Capture(Keypad keypad)
        {
            capturedKeypad?.ReleaseAll();
            capturedKeypad = keypad;
            if (keypad != null) await JS.InvokeVoidAsync("exuarchEditor.focus", runElement);
        }
        private void OnKeyUp(KeyboardEventArgs e)
        {
            if (capturedKeypad != null && KeypadKey(e.Key) is Keypad.Keys key) capturedKeypad.Release(key);
        }
        private void ReleaseKeys()
        {
            foreach (var keypad in Machine.Devices.OfType<Keypad>()) keypad.ReleaseAll();
        }
        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (capturedKeypad != null)
            {
                if (e.Key == "Escape")
                {
                    capturedKeypad.ReleaseAll();
                    capturedKeypad = null;
                    return;
                }
                if (KeypadKey(e.Key) is Keypad.Keys key)
                {
                    capturedKeypad.Press(key);
                    return;
                }
            }
            switch (e.Key)
            {
                case " ": await ToggleRun(); break;
                case "m": case "M": maxSpeed = !maxSpeed; break;
                case "ArrowRight" when e.ShiftKey: StepInstruction(); break;
                case "ArrowRight": TickOnce(); break;
                case "r": case "R": await Restart(); break;
            }
        }
        private void ToggleBreakpoint(int address)
        {
            if (!breakpoints.Remove(address)) breakpoints.Add(address);
        }

        // ---- Tick interpretation ----

        private TickRecord Last { get { return Machine.LastTick; } }
        private BusTransfer TransferOn(string busId)
        {
            return Last?.Transfers.FirstOrDefault(t => t.Bus == busId && t.Driver != null);
        }
        private bool Drives(string deviceId, string busId)
        {
            return TransferOn(busId)?.Driver == deviceId;
        }
        private bool Reads(string deviceId, string busId)
        {
            return TransferOn(busId)?.Readers.Contains(deviceId) == true;
        }
        private bool IsActive(string deviceId)
        {
            return Last?.Signals.Any(s => s.StartsWith(deviceId + ".")) == true;
        }
        private bool Changed(string deviceId)
        {
            return Last?.Changes.Any(c => c.Device == deviceId || c.Device.StartsWith(deviceId + ".")) == true
                || Last?.Writes.Any(w => w.Device == deviceId) == true;
        }
        private IEnumerable<string> SignalsOf(string deviceId)
        {
            return Last?.Signals.Where(s => s.StartsWith(deviceId + ".")).Select(s => s.Substring(deviceId.Length + 1)) ?? Enumerable.Empty<string>();
        }
        // Every value is a 16 bit word: four hex digits.
        private const int Digits = 4;
        private static string Hex(int value) => value.ToString("X4");

        // What runs there now, which on a machine that rewrites or moves its program is not always what was assembled.
        private ListingLine ListingAt(int? address)
        {
            return address == null ? null : Machine.InstructionAt(address.Value);
        }
        private static string SourceText(ListingLine line)
        {
            if (line == null) return "";
            var operands = line.Operands.Length == 0 ? "" : " " + string.Join(", ", line.Operands);
            return $"{line.Mnemonic}{operands}";
        }
        private InstructionDefinition InstructionNamed(string mnemonic)
        {
            return mnemonic == null ? null : Machine.DecoderRom.Microcode.FindInstruction(mnemonic);
        }

        // The instruction whose steps the timeline shows: the one that ran last, or the next one before the first tick.
        private (InstructionDefinition Instruction, int Status, int? RanIndex) TimelineInstruction
        {
            get
            {
                if (Last?.Instruction != null) return (InstructionNamed(Last.Instruction), Last.Status, Last.StepIndex);
                var next = Machine.NextStep;
                return (next?.Instruction, Machine.NextDecoderStatus, null);
            }
        }

        // ---- Schematic geometry ----

        private DeviceTypeInfo Info(DeviceDefinition device)
        {
            return Registry.Info(device.Type) ?? new DeviceTypeInfo { Type = device.Type, Ports = device.Ports().Select(p => p.Key).ToList() };
        }
        private string BusColor(string busId)
        {
            var index = Machine.Definition.Buses.FindIndex(b => b.Id == busId);
            return Palette.Bus(index);
        }
        private static string CategoryColor(DeviceTypeInfo info)
        {
            return Palette.Category(info);
        }
        // The drawing's geometry, the same as the hardware design canvas uses.
        private SchematicLayout Layout
        {
            get
            {
                if (!ReferenceEquals(layoutFor, Machine.Definition)) { layoutFor = Machine.Definition; layout = new SchematicLayout(Machine.Definition, Registry); }
                return layout;
            }
        }
        private SchematicLayout layout;
        private MachineDefinition layoutFor;
        private static string N(double value) => FormattableString.Invariant($"{value:0.#}");
        private static string Px(double value) => FormattableString.Invariant($"{value:0.#}px");

        // ---- Memory view ----

        private IEnumerable<IBusDevice> MemoryDevices { get { return Machine.Devices.Where(d => d is MemoryModule || d is MMU); } }
        private MemoryModule MemoryModule
        {
            get
            {
                var device = Machine.Device(memoryDevice);
                return device switch
                {
                    MMU mmu => mmu.RamBanks[Math.Clamp(memoryBank, 0, mmu.RamBanks.Length - 1)],
                    MemoryModule rom => rom,
                    _ => null,
                };
            }
        }
        private int Bank { get { return Machine.Device(memoryDevice) is MMU ? memoryBank : -1; } }
        private const int PageSize = 256;
        private int memoryPage;
        private int PageCount(MemoryModule module) => (module.Size + PageSize - 1) / PageSize;
        private int Page(MemoryModule module) => Math.Clamp(memoryPage, 0, PageCount(module) - 1);
        private void GoToMar(MemoryModule module) { memoryPage = module.memoryAddress / PageSize; }
        private static int AddressDigits(MemoryModule module) => Math.Max(2, ((int)Math.Ceiling(Math.Log2(Math.Max(2, module.Size))) + 3) / 4);
        // Widest listing line, in cells, to size the listing's cell column.
        // The label column is as wide as the longest label and its colon, so the code after it lines up.
        private int ListingLabelChars { get { return Machine.Assembler.Listing.Select(l => (l.Label?.Length ?? 0) + 1).DefaultIfEmpty(1).Max(); } }
        // What the line assembled to, shown on hover so the code has the width.
        private string CellsTitle(ListingLine line)
        {
            if (line.Cells.Length == 0) return null;
            return $"{Hex(line.Address)}: {string.Join(" ", line.Cells.Select(b => Hex(b)))}";
        }
        // The program counter is whichever register the fetch routine puts on the bus to address program memory:
        // any register can be one, its role comes from the microcode.
        private IEnumerable<int> ProgramCounterValues
        {
            get
            {
                var memory = Machine.Definition.ProgramMemory;
                var fetch = Machine.DecoderRom.Microcode?.Fetch;
                if (memory == null || fetch == null) yield break;
                foreach (var step in fetch.Steps.Where(s => s.Signals.Contains($"{memory}.loadmar")))
                {
                    foreach (var signal in step.Signals.Where(s => s.EndsWith(".output")))
                    {
                        if (Machine.Device(signal.Substring(0, signal.Length - ".output".Length)) is Register register) yield return register.Data;
                    }
                }
            }
        }
        // What the memory panel highlights, worked out once per render: PC values, the current instruction's cells,
        // and the cells written in the last tick and in the ticks just before it.
        private class MemoryMarks
        {
            public readonly HashSet<int> ProgramCounters = new HashSet<int>();
            public int CurrentStart = -1, CurrentEnd = -1;
            public readonly HashSet<int> Written = new HashSet<int>();
            public readonly HashSet<int> Recent = new HashSet<int>();
        }
        private MemoryMarks MarksFor(MemoryModule module)
        {
            var marks = new MemoryMarks();
            if (memoryDevice == Machine.Definition.ProgramMemory)
            {
                marks.ProgramCounters.UnionWith(ProgramCounterValues);
                var current = ListingAt(Machine.CurrentInstructionAddress);
                if (current != null) (marks.CurrentStart, marks.CurrentEnd) = (current.Address, current.Address + current.Cells.Length);
            }
            var bank = Bank;
            foreach (var write in Last?.Writes ?? Enumerable.Empty<MemoryWrite>())
            {
                if (write.Device == memoryDevice && write.Bank == bank) marks.Written.Add(write.Address);
            }
            var history = Machine.History;
            for (int i = Math.Max(0, history.Count - 30); i < history.Count; i++)
            {
                foreach (var write in history[i].Writes)
                {
                    if (write.Device == memoryDevice && write.Bank == bank) marks.Recent.Add(write.Address);
                }
            }
            return marks;
        }
        private static string CellClass(int address, MemoryModule module, MemoryMarks marks)
        {
            var classes = "";
            if (module.memoryAddress == address) classes += " mar";
            if (marks.ProgramCounters.Contains(address)) classes += " pc";
            if (address >= marks.CurrentStart && address < marks.CurrentEnd) classes += " current";
            if (marks.Written.Contains(address)) classes += " written";
            else if (marks.Recent.Contains(address)) classes += " recent";
            if (module.ValueAt(address) == 0) classes += " zero";
            return classes;
        }

        // ---- Control lines and decoder ROM ----

        // Every control line of the machine, grouped by device, in definition order.
        private List<(DeviceDefinition Device, string Color, List<string> Lines)> ControlLines
        {
            get
            {
                var groups = new List<(DeviceDefinition, string, List<string>)>();
                foreach (var device in Machine.Definition.Devices)
                {
                    var info = Info(device);
                    var lines = info.ControlLines.Count > 0 ? info.ControlLines.Select(l => l.Name).ToList() : Machine.Device(device.Id)?.SignalLines() ?? new List<string>();
                    if (lines.Count > 0) groups.Add((device, CategoryColor(info), lines));
                }
                return groups;
            }
        }
        private bool LineActive(string deviceId, string line)
        {
            return Last?.Signals.Contains($"{deviceId}.{line}") == true;
        }
        private int DecoderStatus { get { return (Last?.Status ?? Machine.NextDecoderStatus) & DecoderRom.StatusMask; } }
        private int DecoderStep { get { return Last?.MicroStep ?? Machine.MicroStepRegister; } }
        private int NextAddress { get { return DecoderRom.RomAddress(Machine.NextDecoderStatus, Machine.MicroStepRegister); } }
        private const int StepBits = DecoderRom.StepBits;
        private static string RomHex(int address) => address.ToString("X5");

        // ROM rows to show: the fetch block and the block the decoder just used, or the whole ROM.
        private List<(int Address, string Label, bool BlockStart, HashSet<string> Signals)> RomRows
        {
            get
            {
                var rom = Machine.DecoderRom;
                var blocks = rom.Blocks.Where((b, i) => wholeRom || i == 0 || (DecoderStep >= b.Base && DecoderStep < b.Base + b.Count)).ToList();
                var rows = new List<(int, string, bool, HashSet<string>)>();
                foreach (var block in blocks)
                {
                    for (int offset = 0; offset < block.Count; offset++)
                    {
                        var address = block.Base + offset;
                        var signals = new HashSet<string>(rom.FetchInstruction(DecoderStatus, address).Select(m => $"{m.DeviceID}.{m.Function}"));
                        rows.Add((address, $"{block.Instruction.Mnemonic}.{offset + 1}", offset == 0, signals));
                    }
                }
                return rows;
            }
        }
        private static string Bits(int value, int count)
        {
            return Convert.ToString(value, 2).PadLeft(count, '0');
        }

        // ---- Trace ----

        private string DescribeTransfers(TickRecord tick)
        {
            var parts = tick.Transfers.Where(t => t.Driver != null)
                .Select(t => $"{t.Driver} → {t.Bus} {Hex(t.Value)}" + (t.Readers.Count > 0 ? $" → {string.Join(", ", t.Readers)}" : ""));
            return string.Join(" · ", parts);
        }
        private string DescribeChanges(TickRecord tick)
        {
            var parts = tick.Changes.Select(c => $"{c.Device} {Hex(c.Before)}→{Hex(c.After)}")
                .Concat(tick.Writes.Select(w => $"{w.Device}{(w.Bank >= 0 ? $"[{w.Bank}]" : "")}[{Hex(w.Address)}]={Hex(w.Value)}"));
            return string.Join(" · ", parts);
        }
    }
}
