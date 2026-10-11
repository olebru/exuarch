using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class MultiplyAccumulate : ControlLineDevice, IBusDevice, IPassiveDevice
    {
        public int A { get; private set; }
        public int B { get; private set; }
        public long Accumulator { get; private set; }
        public const int DefaultFractionBits = 8;
        public int Shift { get; }
        public int Result { get { return (int)Math.Clamp(Accumulator >> Shift, short.MinValue, short.MaxValue); } }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool output;
        private readonly LatchedLines loads = new LatchedLines();
        private Func<long> nextAccumulator;

        public MultiplyAccumulate(string DeviceName, string DeviceID, Bus bus, int shift = DefaultFractionBits)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            Shift = shift;
            Func<long> mul = () => (long)A * B, mac = () => Accumulator + (long)A * B, div = Divide;
            ControlLines
                .Add("loada", loads.Add(() => A = Signed(bus.Data)))
                .Add("loadb", loads.Add(() => B = Signed(bus.Data)))
                .Add("mul", () => Start(mul))
                .Add("mac", () => Start(mac))
                .Add("div", () => Start(div))
                .Add("output", () => output = true);
        }

        private static int Signed(int word) { return (short)(word & Bus.Mask); }
        private long Divide()
        {
            if (B == 0) return Accumulator < 0 ? int.MinValue : int.MaxValue;
            return (Accumulator << Shift) / B;
        }
        private void Start(Func<long> next)
        {
            if (nextAccumulator != null && nextAccumulator != next) throw new Exception($"{deviceID}: only one of mul, mac and div can run in a tick.");
            nextAccumulator = next;
        }

        public void Drive()
        {
            if (output)
            {
                bus.Data = Result;
                output = false;
            }
        }
        public void Latch()
        {
            loads.Latch();
            if (nextAccumulator != null) Accumulator = nextAccumulator();
            Accumulator = Math.Clamp(Accumulator, int.MinValue, int.MaxValue);
            nextAccumulator = null;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
    }
}
