using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class InstructionRegister : ControlLineDevice, IBusDevice, IObservableState
    {
        public int Data { get; set; }
        public Func<int, int> Dispatch { get; set; }
        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool load, reset;

        public InstructionRegister(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines
                .Add("load", () => load = true)
                .Add("reset", () => reset = true);
        }

        public void Drive()
        {
        }
        public void Latch()
        {
            if (reset) Data = 0;
            else if (load) Data = Dispatch?.Invoke(bus.Data & Bus.Mask) ?? bus.Data & Bus.Mask;
            else Data = (Data + 1) & Bus.Mask;
            load = reset = false;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return false; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Data); }
    }
}
