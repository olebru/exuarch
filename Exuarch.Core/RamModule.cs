using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class RamModule : MemoryModule, IWriteTracked
    {
        private bool load = false;
        // Counts stores, so observers can tell which modules were written in a tick.
        public long WriteCount { get; private set; }
        public int LastWriteAddress { get; private set; } = -1;
        public RamModule(string DeviceName, string DeviceID, Bus ConnectedBus, int size = DefaultSize) : base(DeviceName, DeviceID, ConnectedBus, size)
        {
            ControlLines.Add("load", () => load = true);
        }
        public override void Latch()
        {
            if (load)
            {
                Store(memoryAddress, connectedBus.Data);
                LastWriteAddress = memoryAddress;
                WriteCount++;
                load = false;
            }
            base.Latch();
        }
    }
}
