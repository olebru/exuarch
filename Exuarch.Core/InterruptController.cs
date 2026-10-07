using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace Exuarch.Core
{
    // A device that can ask for an interrupt. TakeInterruptRequest returns true once for each event (a timer
    // period, a key press, a finished job) and clears it.
    public interface IInterruptSource
    {
        bool TakeInterruptRequest();
    }

    // Collects interrupt requests from up to four sources, connected as irq0 to irq3, into pending bits 0 to 3.
    // The decoder reads it as the I condition (see DecoderDefinition.Interrupts): I is 1 when interrupts are
    // enabled and a pending bit is not masked off. What the CPU does then is up to the microcode.
    //   enable / disable   switch interrupts on or off (they start off)
    //   loadmask           take the mask from the bus: bit n set lets irq n interrupt (starts as all four)
    //   output             put the pending bits that the mask lets through on the bus, so a handler can see who asked
    //   ack                clear the pending bits that are set in the bus value
    public class InterruptController : ControlLineDevice, IBusDevice, IObservableState
    {
        public const int Sources = 4;
        public bool Enabled { get; private set; }
        public int Mask { get; private set; } = (1 << Sources) - 1;
        public int Pending { get; private set; }
        public long Requests { get; private set; }
        // True when the CPU should be interrupted: enabled, with a pending request that is not masked off.
        public bool Requesting { get { return Enabled && (Pending & Mask) != 0; } }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private readonly IInterruptSource[] sources;
        private bool enable, disable, loadMask, output, ack;

        public InterruptController(string DeviceName, string DeviceID, Bus bus, IReadOnlyList<IInterruptSource> sources)
        {
            if (sources.Count > Sources) throw new ArgumentException($"An interrupt controller takes at most {Sources} sources.");
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            this.sources = sources.ToArray();
            ControlLines
                .Add("enable", () => enable = true)
                .Add("disable", () => disable = true)
                .Add("loadmask", () => loadMask = true)
                .Add("output", () => output = true)
                .Add("ack", () => ack = true);
        }

        // Requests from the last tick are collected here, before any device latches, so device order does not matter.
        // Compiled optimized at once rather than tiered: this per tick loop is often first run by a machine started after
        // others in the same process, when .NET can leave newly compiled methods unoptimized for a long time, and run
        // that way it made IRQ-16 run 15% slower.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Drive()
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null && sources[i].TakeInterruptRequest())
                {
                    Pending |= 1 << i;
                    Requests++;
                }
            }
            if (output)
            {
                bus.Data = Pending & Mask;
                output = false;
            }
        }
        public void Latch()
        {
            if (loadMask) Mask = bus.Data & ((1 << Sources) - 1);
            if (ack) Pending &= ~bus.Data;
            if (enable) Enabled = true;
            if (disable) Enabled = false;
            enable = disable = loadMask = ack = false;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".pending", () => Pending);
            watch(deviceID + ".enabled", () => Enabled ? 1 : 0);
        }
    }
}
