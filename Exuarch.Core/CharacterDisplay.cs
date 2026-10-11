using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public class CharacterDisplay : ControlLineDevice, IBusDevice, IWriteTracked, IObservableState, IPassiveDevice
    {
        public const byte LineFeed = 0x0A;
        public const byte CarriageReturn = 0x0D;
        public const byte Space = 0x20;
        private const byte LastPrintableAscii = 0x7E;
        private const byte FirstPrintableLatin1Supplement = 0xA0;

        public readonly int Columns;
        public readonly int Rows;
        public readonly byte[] Cells;
        public int Cursor { get; private set; }
        private readonly Bus bus;
        private readonly string deviceID;
        private readonly string deviceName;
        private bool load, clear;

        public CharacterDisplay(string DeviceName, string DeviceID, Bus bus, int columns = 16, int rows = 4)
        {
            if (columns < 1 || rows < 1 || columns * rows > 1024)
            {
                throw new ArgumentException($"A display must have between 1 and 1024 cells, {columns} x {rows} is {columns * rows}.");
            }
            deviceName = DeviceName;
            deviceID = DeviceID;
            this.bus = bus;
            Columns = columns;
            Rows = rows;
            Cells = Enumerable.Repeat(Space, columns * rows).ToArray();
            ControlLines
                .Add("load", () => load = true)
                .Add("clear", () => clear = true);
        }

        public long WriteCount { get; private set; }
        public int LastWriteAddress { get; private set; } = -1;
        public int ValueAt(int address) { return Cells[address]; }

        public void Drive()
        {
        }
        public void Latch()
        {
            if (clear)
            {
                Array.Fill(Cells, Space);
                Cursor = 0;
                clear = false;
            }
            if (load)
            {
                Put((byte)(bus.Data & 0xFF));
                load = false;
            }
        }
        private void Put(byte character)
        {
            int row = Math.Min(Cursor, Cells.Length - 1) / Columns;
            switch (character)
            {
                case LineFeed:
                    if (row == Rows - 1) ScrollUp();
                    else row++;
                    Cursor = row * Columns;
                    break;
                case CarriageReturn:
                    Cursor = row * Columns;
                    break;
                default:
                    if (Cursor == Cells.Length)
                    {
                        ScrollUp();
                        Cursor = (Rows - 1) * Columns;
                    }
                    Cells[Cursor] = character;
                    LastWriteAddress = Cursor;
                    WriteCount++;
                    Cursor++;
                    break;
            }
        }
        private void ScrollUp()
        {
            Array.Copy(Cells, Columns, Cells, 0, Cells.Length - Columns);
            Array.Fill(Cells, Space, Cells.Length - Columns, Columns);
        }

        public static char ToChar(byte value)
        {
            return IsPrintableLatin1(value) ? (char)value : ' ';
        }
        private static bool IsPrintableLatin1(byte value)
        {
            return (value >= Space && value <= LastPrintableAscii) || value >= FirstPrintableLatin1Supplement;
        }
        public string Line(int row)
        {
            return new string(Cells.Skip(row * Columns).Take(Columns).Select(ToChar).ToArray());
        }
        public string Text
        {
            get { return string.Join("\n", Enumerable.Range(0, Rows).Select(Line)); }
        }

        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled() { return false; }
        public void Observe(WatchValue watch) { watch(deviceID + ".cursor", () => Cursor); }
    }

    public interface IWriteTracked
    {
        long WriteCount { get; }
        int LastWriteAddress { get; }
        int ValueAt(int address);
    }
}
