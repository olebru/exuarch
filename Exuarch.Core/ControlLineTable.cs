using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public abstract class ControlLineDevice
    {
        internal ControlLineTable ControlLines { get; } = new ControlLineTable();

        public virtual void Enable(string function) { ControlLines.Enable(function); }
        public List<string> SignalLines() { return ControlLines.Names(); }
    }

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

        public static Action Bind(IBusDevice device, string name)
        {
            return device is ControlLineDevice table && table.ControlLines.Find(name) is Action handler ? handler : () => device.Enable(name);
        }
    }

    internal sealed class LatchedLines
    {
        private readonly List<Action> actions = new List<Action>();
        private int armed;

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
