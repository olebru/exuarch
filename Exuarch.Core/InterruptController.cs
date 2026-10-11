using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace Exuarch.Core
{
    public interface IInterruptSource
    {
        bool TakeInterruptRequest();
    }

    public class InterruptController : ControlLineDevice, IBusDevice, IObservableState
    {
        public const int Sources = 4;
        public bool Enabled { get; private set; }
        public int Mask { get; private set; } = (1 << Sources) - 1;
        public int Pending { get; private set; }
        public long Requests { get; private set; }
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
