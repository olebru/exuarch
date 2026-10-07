using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public  class  Clock : ControlLineDevice, IBusDevice
    {
        public int cycle = 0;
        private string deviceID;
        private bool halted = false;
        private string name;
        public Clock(string Name, string DeviceID)
        {
            deviceID = DeviceID;
            name = Name;
            ControlLines.Add("disable", () => halted = true);
        }
        public void Drive()
        {
        }
        public void Latch()
        {
           cycle++;
        }
        public string DisplayName() { return name; }
        public override void Enable(string function)
        {
            var handler = ControlLines.Find(function) ?? throw new Exception($"Clock does not have a control line function called {function}");
            handler();
        }
        public string ID()
        {
            return deviceID;
        }
        public bool IsHalted() { return halted; }
        public bool IsOutputEnabled()
        {
            return false;
        }
    }
}
