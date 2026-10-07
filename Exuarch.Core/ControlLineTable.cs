using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A device whose control lines are a table of handlers, which the device fills as it is made. A caller can bind a
    // line once and enable it every tick without its name being looked up again; Enable and SignalLines go by the table.
    public abstract class ControlLineDevice
    {
        internal ControlLineTable ControlLines { get; } = new ControlLineTable();

        public virtual void Enable(string function) { ControlLines.Enable(function); }
        public List<string> SignalLines() { return ControlLines.Names(); }
    }

    // A device's control lines, each bound once to what enabling it does. SignalLines lists them in the order they
    // were added.
    internal sealed class ControlLineTable
    {
        private readonly List<string> names = new List<string>();
        private readonly List<Action> handlers = new List<Action>();

        public ControlLineTable Add(string name, Action handler)
        {
            names.Add(name);
            handlers.Add(handler);
            return this;
        }
        public int Count { get { return names.Count; } }
        public string Name(int index) { return names[index]; }
        public Action this[int index] { get { return handlers[index]; } }
        // What enabling the line does, or null when the device has no such line.
        public Action Find(string name)
        {
            int index = names.IndexOf(name);
            return index < 0 ? null : handlers[index];
        }
        public void Enable(string name)
        {
            var handler = Find(name) ?? throw new Exception("Unable to enable the unknown function: " + name);
            handler();
        }
        public List<string> Names() { return new List<string>(names); }

        // What enabling a line of any device does: its bound handler when the device has a table, otherwise a call
        // to Enable, which also reports a line the device does not have when it is enabled.
        public static Action Bind(IBusDevice device, string name)
        {
            return device is ControlLineDevice table && table.ControlLines.Find(name) is Action handler ? handler : () => device.Enable(name);
        }
    }

    // Control lines that act at the end of the tick, in the order they were added whatever order they were enabled
    // in. Enabling one arms it; Latch runs every armed line once.
    internal sealed class LatchedLines
    {
        private readonly List<Action> actions = new List<Action>();
        private int armed;

        // Returns what enabling the line does: arm the action for this tick's latch.
        public Action Add(Action atLatch)
        {
            int bit = 1 << actions.Count;
            actions.Add(atLatch);
            return () => armed |= bit;
        }
        public void Latch()
        {
            int pending = armed;
            armed = 0;
            for (int i = 0; pending != 0; i++, pending >>= 1)
            {
                if ((pending & 1) != 0) actions[i]();
            }
        }
    }
}
