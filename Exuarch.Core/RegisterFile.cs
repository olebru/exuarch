using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // A bank of numbered registers behind one select input, the way most real CPUs hold their registers: an
    // instruction names a register by number (R0, R1, ...) instead of having one opcode per register. select takes
    // a register number from the bus (modulo the count); output, load, reset, inc and dec then act on the selected
    // register like a register's own lines. select takes effect at the end of the tick, after anything else in the
    // same tick has acted on the register selected before. All registers start at 0 and wrap at 16 bits.
    public class RegisterFile : ControlLineDevice, IBusDevice, IObservableState, IRegisterBank, IPassiveDevice
    {
        public const int DefaultCount = 8;
        public int Count { get; }
        public int Selected { get; private set; }
        private readonly int[] values;
        public IReadOnlyList<int> Values { get { return values; } }
        public int this[int register] { get { return values[register]; } }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool output;
        // In this order at the end of the tick: load, reset, inc and dec on the selected register, then select.
        private readonly LatchedLines writes = new LatchedLines();

        public RegisterFile(string DeviceName, string DeviceID, Bus bus, int count = DefaultCount)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            Count = count;
            values = new int[count];
            var load = writes.Add(() => values[Selected] = bus.Data & Bus.Mask);
            var reset = writes.Add(() => values[Selected] = 0);
            var inc = writes.Add(() => values[Selected] = (values[Selected] + 1) & Bus.Mask);
            var dec = writes.Add(() => values[Selected] = (values[Selected] - 1) & Bus.Mask);
            var select = writes.Add(() => Selected = bus.Data % Count);
            ControlLines
                .Add("select", select)
                .Add("output", () => output = true)
                .Add("load", load)
                .Add("reset", reset)
                .Add("inc", inc)
                .Add("dec", dec);
        }

        public void Drive()
        {
            if (output)
            {
                bus.Data = values[Selected];
                output = false;
            }
        }
        public void Latch()
        {
            writes.Latch();
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".select", () => Selected);
            for (int i = 0; i < Count; i++)
            {
                int register = i;
                watch($"{deviceID}.r{register}", () => values[register]);
            }
        }

        // The registers a machine's register operands can name: those of its first register file, or none.
        public static int CountIn(MachineDefinition machine)
        {
            var device = machine?.Devices.FirstOrDefault(d => d.Type == "registerFile");
            return device == null ? 0 : CountOf(device);
        }

        public static int CountOf(DeviceDefinition device)
        {
            if (device.Type != "registerFile") return 0;
            return device.Parameters.TryGetValue("count", out var count) && count.TryGetInt32(out var n) ? n : DefaultCount;
        }
    }
}
