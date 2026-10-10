using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // Five keys, the arrows and space, read like a register: output puts one bit per key on the bus.
    // A key reads as down while it is held, and also once after a press that was released before the CPU
    // looked, so a short tap is not lost at a slow clock. Reading clears those remembered presses.
    public class Keypad : ControlLineDevice, IBusDevice, IInterruptSource, IObservableState, IPassiveDevice
    {
        [Flags]
        public enum Keys { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Space = 16 }
        public static readonly Keys[] All = { Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Space };

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private Keys held;
        private Keys pressed;
        private bool output, read, interruptRequest;

        public Keypad(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines.Add("output", () => output = true);
        }

        // What the CPU reads: the held keys plus presses it has not read yet.
        public int Data { get { return (int)(held | pressed); } }
        public Keys Held { get { return held; } }

        public void Press(Keys key)
        {
            // A key going down asks for an interrupt; holding it (or key repeat) does not ask again.
            if ((held & key) == 0) interruptRequest = true;
            held |= key;
            pressed |= key;
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
            pressed = Keys.None;
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
                pressed = Keys.None;
                read = false;
            }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Data); }
    }
}
