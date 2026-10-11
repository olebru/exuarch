using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    internal sealed class TickPlan
    {
        public int RomAddress;
        public List<MicroInstruction> MicroCode;
        public Action[] Lines;
        public int[] Clocked;
        public string[] Signals;
        public List<string>[] Readers;
        public bool LoadsInstruction;
        public string Instruction;
        public int? StepIndex;

        public void Enable()
        {
            foreach (var line in Lines) line();
        }
    }

    internal interface ITickRecorder
    {
        TickRecord Begin();
        void End(TickRecord record, TickPlan plan);
    }

    internal sealed class LastTickRecorder : ITickRecorder
    {
        private readonly TickRecord reusedRecord = new TickRecord();
        public TickRecord Begin() { return reusedRecord; }
        public void End(TickRecord record, TickPlan plan) { }
    }

    internal sealed class HistoryRecorder : ITickRecorder
    {
        private readonly Bus[] buses;
        private readonly IBusMaster[] masters;
        private readonly List<(string BusId, string ReaderId)> masterReaders = new List<(string, string)>();
        private readonly string[] keys;
        private readonly Func<int>[] reads;
        private readonly int[] before;
        private readonly (string Device, int Bank, IWriteTracked Module)[] memories;
        private readonly long[] writesBefore;

        public HistoryRecorder(Bus[] buses, IReadOnlyList<IBusDevice> devices)
        {
            this.buses = buses;
            masters = devices.OfType<IBusMaster>().ToArray();
            (keys, reads) = WatchedValues(devices);
            before = new int[keys.Length];
            memories = WriteTracked(devices).ToArray();
            writesBefore = new long[memories.Length];
        }

        public RingBuffer<TickRecord> History { get; } = new RingBuffer<TickRecord>(Machine.HistoryLimit);

        private static (string[] Keys, Func<int>[] Reads) WatchedValues(IReadOnlyList<IBusDevice> devices)
        {
            var slots = new Dictionary<string, int>();
            var keys = new List<string>();
            var reads = new List<Func<int>>();
            void Watch(string key, Func<int> read)
            {
                if (slots.TryGetValue(key, out int slot))
                {
                    reads[slot] = read;
                    return;
                }
                slots[key] = keys.Count;
                keys.Add(key);
                reads.Add(read);
            }
            foreach (var device in devices.OfType<IObservableState>()) device.Observe(Watch);
            return (keys.ToArray(), reads.ToArray());
        }

        private static IEnumerable<(string, int, IWriteTracked)> WriteTracked(IReadOnlyList<IBusDevice> devices)
        {
            foreach (var device in devices)
            {
                if (device is IWriteTracked tracked) yield return (device.ID(), MemoryWrite.NoBank, tracked);
                var banks = (device as IBankedMemory)?.WriteTrackedBanks ?? Array.Empty<IWriteTracked>();
                for (int bank = 0; bank < banks.Count; bank++) yield return (device.ID(), bank, banks[bank]);
            }
        }

        public TickRecord Begin()
        {
            for (int i = 0; i < reads.Length; i++) before[i] = reads[i]();
            for (int i = 0; i < memories.Length; i++) writesBefore[i] = memories[i].Module.WriteCount;
            return new TickRecord();
        }

        public void End(TickRecord record, TickPlan plan)
        {
            record.Signals = plan.Signals.ToList();
            AddTransfers(record, plan);
            AddChanges(record);
            AddWrites(record);
            History.Add(record);
        }

        private void AddTransfers(TickRecord record, TickPlan plan)
        {
            masterReaders.Clear();
            foreach (var master in masters) masterReaders.AddRange(master.LastReaders);
            for (int i = 0; i < buses.Length; i++)
            {
                var bus = buses[i];
                record.Transfers.Add(new BusTransfer { Bus = bus.ID, Driver = bus.Writer?.ID(), Value = bus.Data, Readers = WithMasterReaders(bus.ID, plan.Readers[i]) });
            }
        }
        private List<string> WithMasterReaders(string busId, List<string> readers)
        {
            foreach (var (bus, reader) in masterReaders)
            {
                if (bus == busId && !readers.Contains(reader)) readers = new List<string>(readers) { reader };
            }
            return readers;
        }

        private void AddChanges(TickRecord record)
        {
            for (int i = 0; i < reads.Length; i++)
            {
                int after = reads[i]();
                if (after != before[i]) record.Changes.Add(new ValueChange { Device = keys[i], Before = before[i], After = after });
            }
        }

        private void AddWrites(TickRecord record)
        {
            for (int i = 0; i < memories.Length; i++)
            {
                var (device, bank, module) = memories[i];
                if (module.WriteCount == writesBefore[i]) continue;
                record.Writes.Add(new MemoryWrite { Device = device, Bank = bank, Address = module.LastWriteAddress, Value = module.ValueAt(module.LastWriteAddress) });
            }
        }
    }
}
