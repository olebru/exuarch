using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // What the last tick did, as the views read it: which device drove each bus and which read it, which control lines
    // were on and what changed. Before the first tick it did nothing.
    public sealed class LastTickView
    {
        private readonly TickRecord record;

        public LastTickView(TickRecord record)
        {
            this.record = record;
        }

        public int Cycle => record?.Cycle ?? 0;

        // The transfer on a bus, when a device drove it.
        public BusTransfer TransferOn(string busId)
        {
            return record?.Transfers.FirstOrDefault(t => t.Bus == busId && t.Driver != null);
        }

        public bool Drives(string deviceId, string busId)
        {
            return TransferOn(busId)?.Driver == deviceId;
        }

        public bool Reads(string deviceId, string busId)
        {
            return TransferOn(busId)?.Readers.Contains(deviceId) == true;
        }

        // One of the device's control lines was on.
        public bool IsActive(string deviceId)
        {
            return record?.Signals.Any(s => s.StartsWith(deviceId + ".")) == true;
        }

        public bool LineActive(string deviceId, string line)
        {
            return record?.Signals.Contains($"{deviceId}.{line}") == true;
        }

        // The device's control lines that were on, without the device id.
        public IEnumerable<string> SignalsOf(string deviceId)
        {
            return record?.Signals.Where(s => s.StartsWith(deviceId + ".")).Select(s => s.Substring(deviceId.Length + 1)) ?? Enumerable.Empty<string>();
        }

        // The device's value or a part of it changed, or it wrote to a cell.
        public bool Changed(string deviceId)
        {
            return record?.Changes.Any(c => c.Device == deviceId || c.Device.StartsWith(deviceId + ".")) == true
                || record?.Writes.Any(w => w.Device == deviceId) == true;
        }

        // The cell the device wrote, or -1.
        public int WrittenAt(string deviceId)
        {
            return record?.Writes.FirstOrDefault(w => w.Device == deviceId)?.Address ?? -1;
        }
    }
}
