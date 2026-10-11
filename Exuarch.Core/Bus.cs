using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class Bus
    {
        public string ID;
        public string NumberFormat = "X4";
        public List<IBusDevice> devices;
        public int Cycles = 0;
        public const int Width = 16;
        public const int Mask = 0xFFFF;
        public const int SignBit = 0x8000;
        internal IBusDevice ActiveDevice;
        private int data;
        private bool dataWrittenInThisClk = false;
        private IBusDevice writer;
        public Bus(string id = "bus")
        {
            ID = id;
            devices = new List<IBusDevice>();
        }
        public int Data
        {
            get
            {
                return data;
            }
            set
            {
                if (dataWrittenInThisClk)
                {
                    throw new Exception($"Puff of blue smoke exception, multiple bus devices has output enabled at the same time on bus '{ID}': {writer?.ID() ?? "unknown"}, {ActiveDevice?.ID() ?? "unknown"}");
                }
                data = value & Mask;
                dataWrittenInThisClk = true;
                writer = ActiveDevice;
            }
        }
        public IBusDevice Writer { get { return writer; } }
        internal void BeginTick()
        {
            data = 0;
            dataWrittenInThisClk = false;
            writer = null;
            ActiveDevice = null;
        }
        internal void EndTick()
        {
            ActiveDevice = null;
            Cycles++;
        }
        public void Clk()
        {
            Clocking.Tick(new[] { this }, devices);
        }
    }
}
