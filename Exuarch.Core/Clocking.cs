using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public static class Clocking
    {
        // The allocation free form used by Machine for every tick.
        public static void Tick(Bus[] buses, IBusDevice[] devices)
        {
            foreach (var bus in buses) bus.BeginTick();
            foreach (var device in devices)
            {
                foreach (var bus in buses) bus.ActiveDevice = device;
                device.Drive();
            }
            foreach (var bus in buses) bus.ActiveDevice = null;
            foreach (var device in devices) device.Latch();
            foreach (var bus in buses) bus.EndTick();
        }

        // As above, for a machine that knows which buses each device is on (busesOf, parallel to devices): a device
        // is the active one only on those, the only buses it can drive.
        internal static void Tick(Bus[] buses, IBusDevice[] devices, Bus[][] busesOf)
        {
            foreach (var bus in buses) bus.BeginTick();
            for (int i = 0; i < devices.Length; i++)
            {
                foreach (var bus in busesOf[i]) bus.ActiveDevice = devices[i];
                devices[i].Drive();
            }
            foreach (var bus in buses) bus.ActiveDevice = null;
            foreach (var device in devices) device.Latch();
            foreach (var bus in buses) bus.EndTick();
        }

        internal static void Tick(Bus[] buses, IBusDevice[] devices, Bus[][] busesOf, int[] clocked)
        {
            foreach (var bus in buses) bus.BeginTick();
            foreach (var i in clocked)
            {
                foreach (var bus in busesOf[i]) bus.ActiveDevice = devices[i];
                devices[i].Drive();
            }
            foreach (var bus in buses) bus.ActiveDevice = null;
            foreach (var i in clocked) devices[i].Latch();
            foreach (var bus in buses) bus.EndTick();
        }

        public static void Tick(IReadOnlyCollection<Bus> buses, IEnumerable<IBusDevice> devices)
        {
            var deviceList = devices.ToList();
            foreach (var bus in buses) bus.BeginTick();
            foreach (var device in deviceList)
            {
                foreach (var bus in buses) bus.ActiveDevice = device;
                device.Drive();
            }
            foreach (var bus in buses) bus.ActiveDevice = null;
            foreach (var device in deviceList)
            {
                device.Latch();
            }
            foreach (var bus in buses) bus.EndTick();
        }
    }
}
