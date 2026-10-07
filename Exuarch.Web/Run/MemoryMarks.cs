using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // What the memory panel highlights, worked out once per drawing: the program counter's values and the current
    // instruction's cells in program memory, and the cells written in the last tick and in the ticks just before it.
    public sealed class MemoryMarks
    {
        // How many ticks back a write is still shown as recent.
        private const int RecentTicks = 30;

        public HashSet<int> ProgramCounters { get; } = new HashSet<int>();
        public int CurrentStart { get; private set; } = -1;
        public int CurrentEnd { get; private set; } = -1;
        public HashSet<int> Written { get; } = new HashSet<int>();
        public HashSet<int> Recent { get; } = new HashSet<int>();

        // The marks on one memory, by device id, and the bank of it shown (-1 for memory without banks).
        public static MemoryMarks Build(Machine machine, string deviceId, int bank)
        {
            var marks = new MemoryMarks();
            if (deviceId == machine.Definition.ProgramMemory) marks.MarkProgram(machine);
            marks.Written.UnionWith(WritesTo(machine.LastTick?.Writes ?? Enumerable.Empty<MemoryWrite>(), deviceId, bank));
            var history = machine.History;
            marks.Recent.UnionWith(WritesTo(history.Skip(history.Count - RecentTicks).SelectMany(tick => tick.Writes), deviceId, bank));
            return marks;
        }

        private void MarkProgram(Machine machine)
        {
            ProgramCounters.UnionWith(ProgramCounterValues(machine));
            var current = machine.CurrentInstructionAddress is int address ? machine.InstructionAt(address) : null;
            if (current != null) (CurrentStart, CurrentEnd) = (current.Address, current.Address + current.Cells.Length);
        }

        private static IEnumerable<int> WritesTo(IEnumerable<MemoryWrite> writes, string deviceId, int bank)
        {
            return writes.Where(w => w.Device == deviceId && w.Bank == bank).Select(w => w.Address);
        }

        // The program counter is whichever register the fetch routine puts on the bus to address program memory: any
        // register can be one, its role comes from the microcode.
        private static IEnumerable<int> ProgramCounterValues(Machine machine)
        {
            var memory = machine.Definition.ProgramMemory;
            var fetch = machine.DecoderRom.Microcode?.Fetch;
            if (memory == null || fetch == null) return Enumerable.Empty<int>();
            return fetch.Steps.Where(s => s.Signals.Contains($"{memory}.loadmar"))
                .SelectMany(step => step.Signals.Where(s => s.EndsWith(".output")))
                .Select(signal => machine.Device(signal.Substring(0, signal.Length - ".output".Length)))
                .OfType<Register>()
                .Select(register => register.Data);
        }

        // The classes of a cell, and of its character in the ascii column.
        public string Css(int address, MemoryModule module)
        {
            var classes = "";
            if (module.memoryAddress == address) classes += " mar";
            if (ProgramCounters.Contains(address)) classes += " pc";
            if (address >= CurrentStart && address < CurrentEnd) classes += " current";
            if (Written.Contains(address)) classes += " written";
            else if (Recent.Contains(address)) classes += " recent";
            if (module.ValueAt(address) == 0) classes += " zero";
            return classes;
        }
    }
}
