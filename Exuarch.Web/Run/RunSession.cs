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

    // The machine on show in the Run view and what its parts share: running it, a tick at a time or in a run, its
    // breakpoints, the keypad that has the keyboard and what the memory and decoder panels show. The parts change it
    // through it, and Changed tells the view to draw everything again.
    public sealed class RunSession
    {
        private readonly Analytics analytics;
        private readonly Func<Task> focusKeyboard;
        private readonly TimeProvider time;
        private CancellationTokenSource stop = new CancellationTokenSource();

        // focusKeyboard gives the view the keyboard, for a keypad that captures it.
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
        // Run pauses a running machine, so it works unless the machine halted or failed.
        public bool CanRun => State is RunState.Paused or RunState.Running;
        // Tick and Instruction work only while paused.
        public bool CanStep => State == RunState.Paused;

        // Puts a machine on show. Another one than before stops the run, if any, and starts afresh; false when it is the
        // one on show already.
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

        // One tick: false when it stopped the machine, at HLT, at a breakpoint or with an error.
        public bool Tick()
        {
            try
            {
                Machine.SingleStep();
                if (Machine.IsHalted)
                {
                    // However it got there, Run, Tick or Instruction, the program reached HLT.
                    _ = analytics.Halted();
                    return false;
                }
                return !(Machine.LastTick?.FetchedFromAddress is int address && Breakpoints.Contains(address));
            }
            catch (Exception e)
            {
                RuntimeError = $"Cycle {Machine.Cycles + 1}: {e.Message}";
                _ = analytics.RuntimeError(e.Message);
                return false;
            }
        }

        public void TickOnce()
        {
            if (CanStep) Tick();
        }

        // Runs the current instruction and fetches the next.
        public void StepInstruction()
        {
            if (!CanStep) return;
            for (int i = 0; i < 10000; i++)
            {
                if (!Tick() || Machine.LastTick.FetchedFromAddress != null) break;
            }
        }

        // Starts a run, or pauses the one going. A run goes on until paused, halted or at a breakpoint (see RunLoop).
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
            // How long it ran, and how fast the simulator runs flat out when it ran at max speed throughout.
            double total = clock.Seconds;
            _ = analytics.Ran(startedAtMax && Speed.Max, total, total > 0 ? (Machine.Cycles - startCycles) / total : 0);
        }

        public void ChangeSpeed(Action<RunSpeed> change)
        {
            change(Speed);
            NotifyChanged();
        }

        public void ToggleBreakpoint(int address)
        {
            if (!Breakpoints.Remove(address)) Breakpoints.Add(address);
            NotifyChanged();
        }

        // Gives a keypad the keyboard, or with null gives it back.
        public async Task Capture(Keypad keypad)
        {
            Keys.Capture(keypad);
            NotifyChanged();
            if (keypad != null) await focusKeyboard();
        }

        // The view lost the keyboard: no key stays held down.
        public void ReleaseKeys()
        {
            foreach (var keypad in Machine.Devices.OfType<Keypad>()) keypad.ReleaseAll();
        }

        // ---- What the parts look up ----

        // The instruction at the address the last fetch read.
        public ListingLine Current => ListingAt(Machine.CurrentInstructionAddress);

        // What runs there now, which on a machine that rewrites or moves its program is not always what was assembled.
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
