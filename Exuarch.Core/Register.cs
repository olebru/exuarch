using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
   public  class Register : ControlLineDevice, IBusDevice, IObservableState
    {
        // A 16 bit word; inc and dec wrap around.
        public int Data = 0;
        protected Bus connectedBus;
        protected string deviceID = "";
        protected string deviceName = "";
        protected  bool outputEnabled = false;
        // Every write enabled in a tick happens, in the order load, reset, inc, dec.
        private readonly LatchedLines writes = new LatchedLines();
        public Register(string DeviceName, string DeviceID, Bus ConnectedBus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            connectedBus = ConnectedBus;
            ControlLines
                .Add("output", () => outputEnabled = true)
                .Add("load", writes.Add(() => Data = connectedBus.Data))
                .Add("reset", writes.Add(() => Data = 0))
                .Add("inc", writes.Add(() => Data = (Data + 1) & Bus.Mask))
                .Add("dec", writes.Add(() => Data = (Data - 1) & Bus.Mask));
        }
        public virtual void Drive()
        {
            if (outputEnabled)
            {
                connectedBus.Data = Data;
                outputEnabled = false;
            }
        }
        public virtual void Latch()
        {
            writes.Latch();
        }
        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public virtual bool IsOutputEnabled()
        {
            return outputEnabled;
        }
        public void Observe(WatchValue watch)
        {
            watch(deviceID, () => Data);
        }
        public virtual string ToString(int firstColumnPaddedWidth)
        {
            return $"{deviceName}".PadRight(firstColumnPaddedWidth, ' ') + $"= {Data.ToString("X4")}";
        }
    }
}
