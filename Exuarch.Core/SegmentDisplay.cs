namespace Exuarch.Core
{
    public enum SegmentFormat { Decimal, Hex, Signed }

    public class SegmentDisplay : ControlLineDevice, IBusDevice, IObservableState, IPassiveDevice
    {
        public const int Digits = 6;
        private const int HexDigits = 4;
        private const int SignBit = 0x8000;

        public int Value { get; private set; }
        public SegmentFormat Format { get; private set; }
        private readonly string deviceID;
        private readonly string deviceName;
        private readonly LatchedLines writes = new LatchedLines();

        public SegmentDisplay(string DeviceName, string DeviceID, Bus bus)
        {
            deviceName = DeviceName;
            deviceID = DeviceID;
            ControlLines
                .Add("load", writes.Add(() => Show(bus.Data, SegmentFormat.Decimal)))
                .Add("loadhex", writes.Add(() => Show(bus.Data, SegmentFormat.Hex)))
                .Add("loadsigned", writes.Add(() => Show(bus.Data, SegmentFormat.Signed)));
        }

        private void Show(int value, SegmentFormat format)
        {
            Value = value & Bus.Mask;
            Format = format;
        }

        public string Text
        {
            get
            {
                var digits = Format switch
                {
                    SegmentFormat.Hex => Value.ToString("X" + HexDigits),
                    SegmentFormat.Signed => ((Value & SignBit) != 0 ? Value - (Bus.Mask + 1) : Value).ToString(),
                    _ => Value.ToString(),
                };
                return digits.PadLeft(Digits);
            }
        }

        public void Drive()
        {
        }
        public void Latch()
        {
            writes.Latch();
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return false; }
        public void Observe(WatchValue watch) { watch(deviceID, () => Value); }
    }
}
