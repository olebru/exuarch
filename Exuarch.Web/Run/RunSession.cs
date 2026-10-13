using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    public enum RunState { Paused, Running, Halted, Error }

    public sealed class RunSession
    {
        private const int MaxTicksPerInstruction = 10000;

        private readonly Analytics analytics;
        private readonly Func<Task> focusKeyboard;
        private readonly TimeProvider time;
        private CancellationTokenSource stop = new CancellationTokenSource();

        public RunSession(Analytics analytics, DeviceRegistry registry, Func<Task> focusKeyboard, TimeProvider time = null)
        {
            this.analytics = analytics;
            Registry = registry;
            this.focusKeyboard = focusKeyboard;
            this.time = time ?? TimeProvider.System;
            Memory = new MemoryView(this);
            Decoder = new DecoderView(this);
        }

        public event Action Changed;
        public void NotifyChanged() => Changed?.Invoke();

        public Machine Machine { get; private set; }
        public DeviceRegistry Registry { get; }
        public RunSpeed Speed => RunSpeed.Session;
        public SpeedMeter Meter { get; } = new SpeedMeter();
        public HashSet<int> Breakpoints { get; } = new HashSet<int>();
        public HashSet<string> ExpandedBuses { get; } = new HashSet<string>();
        public bool ShowsControlWires { get; private set; }
        public KeypadCapture Keys { get; } = new KeypadCapture();
        public MemoryView Memory { get; }
        public DecoderView Decoder { get; }
        public LastTickView Last => new LastTickView(Machine.LastTick);
        public string RuntimeError { get; private set; }
        public bool Running { get; private set; }

        public RunState State
        {
            get
            {
                if (RuntimeError != null) return RunState.Error;
                if (Machine.IsHalted) return RunState.Halted;
                return Running ? RunState.Running : RunState.Paused;
            }
        }
        public bool CanRun => State is RunState.Paused or RunState.Running;
        public bool CanStep => State == RunState.Paused;

        public bool Show(Machine machine)
        {
            if (ReferenceEquals(Machine, machine)) return false;
            Machine = machine;
            Stop();
            RuntimeError = null;
            Meter.Clear();
            if (machine != null) Memory.PickFor(machine);
            return true;
        }

        public void Stop()
        {
            stop.Cancel();
            Running = false;
        }

        public bool Tick()
        {
            try
            {
                Machine.SingleStep();
                if (Machine.IsHalted)
                {
                    _ = analytics.Halted();
                    return false;
                }
                return !FetchedAtBreakpoint();
            }
            catch (Exception e)
            {
                RuntimeError = $"Cycle {Machine.Cycles + 1}: {e.Message}";
                _ = analytics.RuntimeError(e.Message);
                return false;
            }
        }

        private bool FetchedAtBreakpoint()
        {
            return Machine.LastTick?.FetchedFromAddress is int address && Breakpoints.Contains(address);
        }

        public void TickOnce()
        {
            if (CanStep) Tick();
        }

        public void StepInstruction()
        {
            if (!CanStep) return;
            for (int i = 0; i < MaxTicksPerInstruction; i++)
            {
                if (!Tick() || Machine.LastTick.FetchedFromAddress != null) break;
            }
        }

        public async Task ToggleRun(IRunHost host)
        {
            if (Running)
            {
                stop.Cancel();
                return;
            }
            if (!CanRun) return;
            Running = true;
            stop = new CancellationTokenSource();
            var machine = Machine;
            var clock = new ElapsedClock(time);
            bool startedAtMax = Speed.Max;
            long startCycles = machine.Cycles;
            await new RunLoop(this, host, time).Run(machine, clock, stop.Token);
            if (ReferenceEquals(machine, Machine)) Finish(clock, startedAtMax, startCycles);
            host.Redraw();
        }

        private void Finish(ElapsedClock clock, bool startedAtMax, long startCycles)
        {
            Running = false;
            Meter.FinishRun(clock.Seconds, Machine.Cycles);
            double totalSeconds = clock.Seconds;
            bool maxSpeedThroughout = startedAtMax && Speed.Max;
            double ticksPerSecond = totalSeconds > 0 ? (Machine.Cycles - startCycles) / totalSeconds : 0;
            _ = analytics.Ran(maxSpeedThroughout, totalSeconds, ticksPerSecond);
        }

        public void ChangeSpeed(Action<RunSpeed> change)
        {
            change(Speed);
            NotifyChanged();
        }

        public void ToggleBus(string busId)
        {
            if (!ExpandedBuses.Remove(busId)) ExpandedBuses.Add(busId);
            NotifyChanged();
        }

        public void ToggleControlWires()
        {
            ShowsControlWires = !ShowsControlWires;
            NotifyChanged();
        }

        public void ToggleBreakpoint(int address)
        {
            if (!Breakpoints.Remove(address)) Breakpoints.Add(address);
            NotifyChanged();
        }

        public async Task Capture(Keypad keypad)
        {
            Keys.Capture(keypad);
            NotifyChanged();
            if (keypad != null) await focusKeyboard();
        }

        public void ReleaseKeys()
        {
            foreach (var keypad in Machine.Devices.OfType<Keypad>()) keypad.ReleaseAll();
        }

        public ListingLine Current => ListingAt(Machine.CurrentInstructionAddress);

        private ListingLine ListingAt(int? address)
        {
            return address == null ? null : Machine.InstructionAt(address.Value);
        }

        public InstructionDefinition InstructionNamed(string mnemonic)
        {
            return mnemonic == null ? null : Machine.DecoderRom.Microcode.FindInstruction(mnemonic);
        }

        public DeviceTypeInfo Info(DeviceDefinition device)
        {
            return Registry.InfoFor(device);
        }

        public string BusColor(string busId) => Palette.Bus(Machine.Definition, busId);
    }
}
