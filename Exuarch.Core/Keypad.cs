using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class Keypad : ControlLineDevice, IBusDevice, IInterruptSource, IObservableState, IPassiveDevice
    {
        [Flags]
        public enum Keys { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Space = 16 }
        public static readonly Keys[] All = { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Space };

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private Keys held;
        private Keys unreadPresses;
        private bool output, read, interruptRequest;

        public Keypad(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines.Add("output", () => output = true);
        }

        public int Data { get { return (int)(held | unreadPresses); } }
        public Keys Held { get { return held; } }

        public void Press(Keys key)
        {
            bool wentDown = (held & key) == 0;
            if (wentDown) interruptRequest = true;
            held |= key;
            unreadPresses |= key;
        }
        public bool TakeInterruptRequest()
        {
            var taken = interruptRequest;
            interruptRequest = false;
            return taken;
        }
        public void Release(Keys key)
        {
            held &= ~key;
        }
        public void ReleaseAll()
        {
            held = Keys.None;
            unreadPresses = Keys.None;
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
                unreadPresses = Keys.None;
                read = false;
            }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Data); }
    }
}
