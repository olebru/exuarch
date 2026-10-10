using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A multiply-accumulate unit for fixed point maths: the part of a 3D pipeline the ALU can not do. It takes two
    // signed 16 bit operands from the bus (loada, loadb) and keeps a 32 bit accumulator:
    //   mul  acc = a * b
    //   mac  acc = acc + a * b
    //   div  acc = (acc << shift) / b, so a quotient keeps the same fixed point scale (b = 0 gives the largest value
    //        of acc's sign)
    // output puts acc >> shift on the bus, clamped to -32768..32767. With the default shift of 8 the numbers are
    // 8.8 fixed point: 256 is 1.0, so multiplying a coordinate by a cosine of 256 * cos(angle) and reading the output
    // gives the rotated coordinate. The operations happen at the end of the tick, after loads in the same tick.
    public class MultiplyAccumulate : ControlLineDevice, IBusDevice, IPassiveDevice
    {
        public int A { get; private set; }
        public int B { get; private set; }
        public long Accumulator { get; private set; }
        public int Shift { get; }
        // The value output would put on the bus.
        public int Result { get { return (int)Math.Clamp(Accumulator >> Shift, short.MinValue, short.MaxValue); } }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool output;
        private readonly LatchedLines loads = new LatchedLines();
        // The new accumulator from mul, mac or div; only one of them can run in a tick.
        private Func<long> operation;

        public MultiplyAccumulate(string DeviceName, string DeviceID, Bus bus, int shift = 8)
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
            if (operation != null && operation != next) throw new Exception($"{deviceID}: only one of mul, mac and div can run in a tick.");
            operation = next;
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
            if (operation != null) Accumulator = operation();
            Accumulator = Math.Clamp(Accumulator, int.MinValue, int.MaxValue);
            operation = null;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
    }
}
