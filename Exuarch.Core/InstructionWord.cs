using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class InstructionWord : ControlLineDevice, IBusDevice, IObservableState
    {
        public int Word { get; private set; }
        public InstructionFormat Format { get; set; }

        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool load, output;
        private int unsignedFields, signedFields;

        public InstructionWord(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            ControlLines
                .Add("load", () => load = true)
                .Add("output", () => output = true);
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                int bit = 1 << field;
                ControlLines.Add(FieldLine(field), () => unsignedFields |= bit);
            }
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                int bit = 1 << field;
                ControlLines.Add(SignedFieldLine(field), () => signedFields |= bit);
            }
        }

        public static string FieldLine(int field) => $"field{field}";
        public static string SignedFieldLine(int field) => $"sfield{field}";

        public IReadOnlyList<FieldSlot> Slots => Format?.Slots ?? Array.Empty<FieldSlot>();

        public int Field(int field, bool signed = false)
        {
            var slots = Slots;
            if (field >= slots.Count) return 0;
            return signed ? slots[field].ReadSigned(Word) : slots[field].Read(Word);
        }

        public void Drive()
        {
            if (output) bus.Data = Word;
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                if ((unsignedFields & (1 << field)) != 0) bus.Data = Field(field);
                if ((signedFields & (1 << field)) != 0) bus.Data = Field(field, signed: true);
            }
            output = false;
            unsignedFields = signedFields = 0;
        }

        public void Latch()
        {
            if (load) Word = bus.Data & Bus.Mask;
            load = false;
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return output || unsignedFields != 0 || signedFields != 0; }
        public void Observe(WatchValue watch)
        {
            watch(deviceID, () => Word);
            for (int field = 0; field < InstructionFormat.MaxFields; field++)
            {
                int index = field;
                watch($"{deviceID}.{FieldLine(index)}", () => Field(index));
            }
        }
    }
}
