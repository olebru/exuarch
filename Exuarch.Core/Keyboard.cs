using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public class Keyboard : ControlLineDevice, IBusDevice, IInterruptSource, IObservableState, IPassiveDevice
    {
        public const int Capacity = 32;
        public const int ReleaseBit = 0x80;
        public const int Empty = 0;

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private readonly Queue<int> events = new Queue<int>(Capacity);
        private readonly SortedSet<int> held = new SortedSet<int>();
        private bool output, read, interruptRequest;

        public Keyboard(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines.Add("output", () => output = true);
        }

        public int Data { get { return events.Count > 0 ? events.Peek() : Empty; } }
        public int Count { get { return events.Count; } }
        public IReadOnlyCollection<int> Events { get { return events; } }
        public IReadOnlyCollection<int> Held { get { return held; } }
        public long Dropped { get; private set; }

        public void Press(int scanCode)
        {
            held.Add(scanCode);
            Add(scanCode);
        }

        public void Release(int scanCode)
        {
            if (held.Remove(scanCode)) Add(scanCode | ReleaseBit);
        }

        public void ReleaseAll()
        {
            foreach (var scanCode in held.ToList()) Release(scanCode);
        }

        private void Add(int code)
        {
            if (events.Count == Capacity)
            {
                Dropped++;
                return;
            }
            events.Enqueue(code);
            interruptRequest = true;
        }

        public bool TakeInterruptRequest()
        {
            var taken = interruptRequest;
            interruptRequest = false;
            return taken;
        }

        public void Drive()
        {
            if (output)
            {
                bus.Data = Data;
                output = false;
                read = true;
            }
        }
        public void Latch()
        {
            if (read)
            {
                if (events.Count > 0) events.Dequeue();
                read = false;
            }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID, () => Data);
            watch(deviceID + ".count", () => Count);
        }
    }
}
