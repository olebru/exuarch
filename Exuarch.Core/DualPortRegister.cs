using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class DualPortRegister : ControlLineDevice, IBusDevice, IObservableState, IPassiveDevice
    {
        public int Data;
        public readonly Bus BusA;
        public readonly Bus BusB;
        private string deviceID;
        private string deviceName;
        private bool outputA, outputB;
        private const int PortA = 1, PortB = 2;
        private int loadPortsThisTick;
        private readonly LatchedLines changes = new LatchedLines();
        public DualPortRegister(string DeviceName, string DeviceID, Bus busA, Bus busB)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            BusA = busA;
            BusB = busB;
            ControlLines
                .Add("loada", () => loadPortsThisTick |= PortA)
                .Add("loadb", () => loadPortsThisTick |= PortB)
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
            if (loadPortsThisTick == (PortA | PortB))
            {
                throw new Exception($"{deviceID}: loada and loadb can not be enabled in the same tick.");
            }
            if (loadPortsThisTick != 0) Data = (loadPortsThisTick == PortA ? BusA : BusB).Data;
            loadPortsThisTick = 0;
            changes.Latch();
        }
        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return outputA || outputB; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Data); }
    }
}
