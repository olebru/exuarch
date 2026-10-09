using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class RomModule : MemoryModule
    {
        public IReadOnlyList<int> Contents { get; }

        public RomModule(string DeviceName, string DeviceID, Bus ConnectedBus, int size = DefaultSize, IEnumerable<string> contents = null)
            : base(DeviceName, DeviceID, ConnectedBus, size)
        {
            Contents = RomContents.Cells(contents, size);
            if (Contents.Count > 0) LoadProgram(Contents);
        }
    }
}
