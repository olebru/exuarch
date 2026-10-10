using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ClockingTests
{
    private class CountingDevice : IBusDevice
    {
        private readonly string id;
        public int Drives { get; private set; }
        public int Latches { get; private set; }
        public int Pokes { get; private set; }
        private bool poke;

        public CountingDevice(string id) { this.id = id; }
        public void Drive() { Drives++; }
        public void Latch()
        {
            Latches++;
            if (poke) Pokes++;
            poke = false;
        }
        public string DisplayName() { return id; }
        public void Enable(string function) { if (function == "poke") poke = true; }
        public string ID() { return id; }
        public bool IsOutputEnabled() { return false; }
        public List<string> SignalLines() { return new List<string> { "poke" }; }
    }

    private sealed class PassiveCountingDevice : CountingDevice, IPassiveDevice
    {
        public PassiveCountingDevice(string id) : base(id) { }
    }

    private static Machine Build(params string[] pokedEveryOtherTick)
    {
        var registry = DeviceRegistry.CreateDefault();
        registry.Register("busy", c => new CountingDevice(c.Id));
        registry.Register("idle", c => new PassiveCountingDevice(c.Id));
        var machine = MachineDefinition.FromJson(ExampleData.MACHINE);
        machine.Devices.Add(new DeviceDefinition { Id = "busy", Type = "busy", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "idle", Type = "idle", Bus = "main" });
        var microcode = new MicrocodeDefinition
        {
            Fetch = new InstructionDefinition { Mnemonic = "FETCH", Steps = { new MicroStep { Signals = { "idle.poke" } }, new MicroStep { Signals = { "regi.reset" } } } },
        };
        return new Machine(machine, microcode, "", registry);
    }

    [Fact]
    public void ADeviceThatActsOnItsOwnIsClockedEveryTick()
    {
        var machine = Build();
        for (int i = 0; i < 10; i++) machine.SingleStep();
        var busy = (CountingDevice)machine.Device("busy");

        Assert.Equal((10, 10), (busy.Drives, busy.Latches));
    }

    [Fact]
    public void APassiveDeviceIsClockedOnlyInTicksThatEnableOneOfItsLines()
    {
        var machine = Build();
        for (int i = 0; i < 10; i++) machine.SingleStep();
        var idle = (CountingDevice)machine.Device("idle");

        Assert.Equal((5, 5, 5), (idle.Drives, idle.Latches, idle.Pokes));
    }

    [Fact]
    public void TheDevicesABusMasterDrivesAreClockedEveryTick()
    {
        var package = BuiltInPackages.Get("COPRO-16");
        var machine = new Machine(package.Machine, "GO\nHLT");
        var screen = machine.Device<Framebuffer>("fb");

        Assert.IsAssignableFrom<IPassiveDevice>(screen);
        var clocked = typeof(Machine).GetField("alwaysClocked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(machine) as bool[];
        var index = machine.Devices.ToList().IndexOf(screen);
        Assert.True(clocked![index]);
        Assert.False(clocked[machine.Devices.ToList().IndexOf(machine.Device("a"))]);
    }
}
