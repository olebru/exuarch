using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class MultiportRegisterFile : ControlLineDevice, IBusDevice, IObservableState, IRegisterBank, IPassiveDevice
    {
        public const string PortA = "a";
        public const string PortB = "b";
        public const string WritePort = "w";
        public const string SecondWritePort = "v";
        public const int DefaultWriteField = 0;
        public const int DefaultReadAField = 1;
        public const int DefaultReadBField = 2;
        public const int DefaultSecondWriteField = DefaultReadAField;

        public int Count { get; }
        private readonly int[] values;
        public IReadOnlyList<int> Values { get { return values; } }
        public int this[int register] { get { return values[register]; } }

        private readonly string deviceID;
        private readonly string deviceName;
        private readonly Bus busA;
        private readonly Bus busB;
        private readonly Bus writeBus;
        private readonly Bus secondWriteBus;
        private readonly InstructionWord word;
        private readonly int readAField;
        private readonly int readBField;
        private readonly int writeField;
        private readonly int secondWriteField;
        private bool outputA, outputB, load, secondLoad;
        private int loadTarget, secondLoadTarget;

        public MultiportRegisterFile(string DeviceName, string DeviceID, Bus busA, Bus busB, Bus writeBus, InstructionWord word,
            int count = RegisterFile.DefaultCount, int readAField = DefaultReadAField, int readBField = DefaultReadBField, int writeField = DefaultWriteField,
            Bus secondWriteBus = null, int secondWriteField = DefaultSecondWriteField)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.busA = busA;
            this.busB = busB;
            this.writeBus = writeBus;
            this.word = word;
            this.readAField = readAField;
            this.readBField = readBField;
            this.writeField = writeField;
            this.secondWriteBus = secondWriteBus;
            this.secondWriteField = secondWriteField;
            Count = count;
            values = new int[count];
            ControlLines
                .Add("outputa", () => outputA = true)
                .Add("outputb", () => outputB = true)
                .Add("loadw", () => { load = true; loadTarget = WriteSelected; })
                .Add("loadv", () => { secondLoad = true; secondLoadTarget = SecondWriteSelected; });
        }

        public int ReadASelected { get { return RegisterIn(readAField); } }
        public int ReadBSelected { get { return RegisterIn(readBField); } }
        public int WriteSelected { get { return RegisterIn(writeField); } }
        public int SecondWriteSelected { get { return RegisterIn(secondWriteField); } }
        public bool HasSecondWritePort { get { return secondWriteBus != null; } }

        private int RegisterIn(int field)
        {
            return word.Field(field) % Count;
        }

        public void Drive()
        {
            if (outputA) busA.Data = values[ReadASelected];
            if (outputB) busB.Data = values[ReadBSelected];
            outputA = outputB = false;
        }

        public void Latch()
        {
            if (secondLoad && secondWriteBus == null) throw new Exception($"{deviceID}: loadv needs bus port '{SecondWritePort}' connected to a bus.");
            if (load && secondLoad && loadTarget == secondLoadTarget) throw new Exception($"{deviceID}: loadw and loadv both write R{loadTarget} in the same tick.");
            if (load) values[loadTarget] = writeBus.Data & Bus.Mask;
            if (secondLoad) values[secondLoadTarget] = secondWriteBus.Data & Bus.Mask;
            load = secondLoad = false;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return outputA || outputB; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".reada", () => ReadASelected);
            watch(deviceID + ".readb", () => ReadBSelected);
            watch(deviceID + ".write", () => WriteSelected);
            if (HasSecondWritePort) watch(deviceID + ".write2", () => SecondWriteSelected);
            for (int i = 0; i < Count; i++)
            {
                int register = i;
                watch($"{deviceID}.r{register}", () => values[register]);
            }
        }
    }
}
