using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A register connected to two buses, "a" and "b". It can latch from either bus and drive either bus,
    // so a value crosses between buses in two ticks (load on one side, output on the other).
    public class DualPortRegister : ControlLineDevice, IBusDevice, IObservableState
    {
        public int Data;
        public readonly Bus BusA;
        public readonly Bus BusB;
        private string deviceID;
        private string deviceName;
        private bool outputA, outputB;
        // The ports a load was enabled on this tick, PortA and PortB as bits. Only one may be.
        private const int PortA = 1, PortB = 2;
        private int loads;
        // After a load: reset, inc and dec, in that order.
        private readonly LatchedLines changes = new LatchedLines();
        public DualPortRegister(string DeviceName, string DeviceID, Bus busA, Bus busB)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            BusA = busA;
            BusB = busB;
            ControlLines
                .Add("loada", () => loads |= PortA)
                .Add("loadb", () => loads |= PortB)
                .Add("outputa", () => outputA = true)
                .Add("outputb", () => outputB = true)
                .Add("reset", changes.Add(() => Data = 0))
                .Add("inc", changes.Add(() => Data = (Data + 1) & Bus.Mask))
                .Add("dec", changes.Add(() => Data = (Data - 1) & Bus.Mask));
        }
        public void Drive()
        {
            if (outputA)
            {
                BusA.Data = Data;
                outputA = false;
            }
            if (outputB)
            {
                BusB.Data = Data;
                outputB = false;
            }
        }
        public void Latch()
        {
            if (loads == (PortA | PortB))
            {
                throw new Exception($"{deviceID}: loada and loadb can not be enabled in the same tick.");
            }
            if (loads != 0) Data = (loads == PortA ? BusA : BusB).Data;
            loads = 0;
            changes.Latch();
        }
        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return outputA || outputB; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Data); }
    }
}
