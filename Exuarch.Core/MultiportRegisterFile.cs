using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class MultiportRegisterFile : ControlLineDevice, IBusDevice, IObservableState, IRegisterBank, IPassiveDevice
    {
        public const string PortA = "a";
        public const string PortB = "b";
        public const string WritePort = "w";
        public const int DefaultWriteField = 0;
        public const int DefaultReadAField = 1;
        public const int DefaultReadBField = 2;

        public int Count { get; }
        private readonly int[] values;
        public IReadOnlyList<int> Values { get { return values; } }
        public int this[int register] { get { return values[register]; } }

        private readonly string deviceID;
        private readonly string deviceName;
        private readonly Bus busA;
        private readonly Bus busB;
        private readonly Bus writeBus;
        private readonly InstructionWord word;
        private readonly int readAField;
        private readonly int readBField;
        private readonly int writeField;
        private bool outputA, outputB, load;
        private int loadTarget;

        public MultiportRegisterFile(string DeviceName, string DeviceID, Bus busA, Bus busB, Bus writeBus, InstructionWord word,
            int count = RegisterFile.DefaultCount, int readAField = DefaultReadAField, int readBField = DefaultReadBField, int writeField = DefaultWriteField)
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
            Count = count;
            values = new int[count];
            ControlLines
                .Add("outputa", () => outputA = true)
                .Add("outputb", () => outputB = true)
                .Add("loadw", () => { load = true; loadTarget = WriteSelected; });
        }

        public int ReadASelected { get { return RegisterIn(readAField); } }
        public int ReadBSelected { get { return RegisterIn(readBField); } }
        public int WriteSelected { get { return RegisterIn(writeField); } }

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
            if (load) values[loadTarget] = writeBus.Data & Bus.Mask;
            load = false;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return outputA || outputB; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID + ".reada", () => ReadASelected);
            watch(deviceID + ".readb", () => ReadBSelected);
            watch(deviceID + ".write", () => WriteSelected);
            for (int i = 0; i < Count; i++)
            {
                int register = i;
                watch($"{deviceID}.r{register}", () => values[register]);
            }
        }
    }
}
