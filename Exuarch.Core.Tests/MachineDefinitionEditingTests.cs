using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class MachineDefinitionEditingTests
{
    private static MachineDefinition Default() => MachineDefinition.FromJson(ExampleData.MACHINE);

    [Fact]
    public void RenameDeviceUpdatesEveryReference()
    {
        var d = Default();
        d.RenameDevice("regsta", "flags");
        d.RenameDevice("regi", "step");
        d.RenameDevice("clk", "clock");
        d.RenameDevice("mem", "ram");
        d.RenameDevice("rega", "acc");

        Assert.Equal("flags", d.FindDevice("alu").Connections["status"]);
        Assert.Equal("acc", d.FindDevice("alu").Connections["a"]);
        Assert.Equal("flags", d.Decoder.Status);
        Assert.Equal("step", d.Decoder.InstructionRegister);
        Assert.Equal("clock", d.Halt);
        Assert.Equal("ram", d.ProgramMemory);
        Machine.FromJson(d.ToJson(), ExampleData.ROMDATA.Replace("\trega\t", "\tacc\t").Replace("\tregsta\t", "\tflags\t")
            .Replace("\tregi\t", "\tstep\t").Replace("\tclk\t", "\tclock\t").Replace("\tmem\t", "\tram\t"), ExampleData.SRC);
    }

    [Theory]
    [InlineData("rega")]
    [InlineData("main")]
    [InlineData("")]
    [InlineData("two words")]
    public void RenameRejectsInvalidOrUsedIds(string newId)
    {
        Assert.Throws<ArgumentException>(() => Default().RenameDevice("regb", newId));
    }

    [Fact]
    public void RemoveDeviceClearsReferences()
    {
        var d = Default();
        d.RemoveDevice("regb");
        d.RemoveDevice("regsta");
        d.RemoveDevice("clk");

        Assert.Null(d.FindDevice("regb"));
        Assert.False(d.FindDevice("alu").Connections.ContainsKey("b"));
        Assert.False(d.FindDevice("alu").Connections.ContainsKey("status"));
        Assert.Null(d.Decoder.Status);
        Assert.Null(d.Halt);
    }

    [Fact]
    public void RenameAndRemoveBusUpdatePorts()
    {
        var d = Default();
        d.Buses.Add(new BusDefinition { Id = "io" });
        d.Devices.Add(new DeviceDefinition { Id = "bridge", Type = "dualPortRegister" });
        d.FindDevice("bridge").SetPortBus("a", "main");
        d.FindDevice("bridge").SetPortBus("b", "io");

        d.RenameBus("main", "data");
        Assert.Equal("data", d.FindDevice("rega").Bus);
        Assert.Equal("data", d.FindDevice("bridge").GetPortBus("a"));

        d.RemoveBus("io");
        Assert.Null(d.FindDevice("bridge").GetPortBus("b"));
        Assert.Equal("data", d.FindDevice("bridge").GetPortBus("a"));
    }

    [Fact]
    public void NextFreeIdCountsUp()
    {
        var d = Default();
        Assert.Equal("reg", d.NextFreeId("reg"));
        Assert.Equal("alu2", d.NextFreeId("alu"));
        d.Devices.Add(new DeviceDefinition { Id = "alu2", Type = "alu" });
        Assert.Equal("alu3", d.NextFreeId("alu"));
    }

    [Fact]
    public void LayoutRoundTripsAndEmptyCollectionsAreOmitted()
    {
        var d = Default();
        d.FindDevice("rega").Layout = new Position { X = 120, Y = 40 };
        var json = d.ToJson();
        Assert.Equal(120, MachineDefinition.FromJson(json).FindDevice("rega").Layout.X);
        Assert.DoesNotContain("\"connections\": {}", json);
        Assert.DoesNotContain("\"parameters\": {}", json);
        Assert.DoesNotContain("\"buses\": {}", json);
    }

    [Fact]
    public void RegistryDescribesEveryBuiltInType()
    {
        var registry = DeviceRegistry.CreateDefault();
        Assert.Equal(registry.Types.OrderBy(t => t), registry.TypeInfos.Select(i => i.Type).OrderBy(t => t));
        Assert.Equal(new[] { "a", "b" }, registry.Info("dualPortRegister").Ports);
        Assert.Empty(registry.Info("clock").Ports);
        Assert.Equal(new[] { "a", "b", "status" }, registry.Info("alu").Connections.Select(c => c.Name));
    }

    [Fact]
    public void EnsureLayoutPlacesUnpositionedElementsAroundTheirBus()
    {
        var d = Default();
        d.Buses.Add(new BusDefinition { Id = "io" });
        d.Devices.Add(new DeviceDefinition { Id = "out", Type = "register", Bus = "io" });
        foreach (var device in d.Devices) device.Layout = null;
        foreach (var bus in d.Buses) bus.Layout = null;
        d.Decoder.Layout = null;
        d.EnsureLayout();
        Assert.True(d.FindBus("io").Layout.Y > d.FindBus("main").Layout.Y);
        var output = d.FindDevice("out").Layout;
        Assert.True(output.Y > d.FindBus("main").Layout.Y);
        Assert.NotNull(d.Decoder.Layout);
        AssertNoOverlaps(d);

        d.FindDevice("rega").Layout = new Position { X = 999, Y = 999 };
        d.Devices.Add(new DeviceDefinition { Id = "late", Type = "register", Bus = "io" });
        d.EnsureLayout();
        Assert.Equal(999, d.FindDevice("rega").Layout.X);
        Assert.Equal(output.Y, d.FindDevice("out").Layout.Y);
        var late = d.FindDevice("late").Layout;
        Assert.True(System.Math.Abs(late.Y + SchematicLayout.CardMinHeight / 2 - d.FindBus("io").Layout.Y) < 200);
        AssertNoOverlaps(d);

        d.EnsureLayout(force: true);
        Assert.NotEqual(999, d.FindDevice("rega").Layout.X);
        AssertNoOverlaps(d);
    }

    private static void AssertNoOverlaps(MachineDefinition d)
    {
        var cards = new SchematicLayout(d, DeviceRegistry.CreateDefault()).Cards().ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            for (int j = i + 1; j < cards.Count; j++)
            {
                var (a, b) = (cards[i], cards[j]);
                Assert.False(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height, $"{a.Id} and {b.Id} overlap");
            }
            foreach (var bus in d.Buses) Assert.False(bus.Layout.Y > cards[i].Y && bus.Layout.Y < cards[i].Y + cards[i].Height, $"{cards[i].Id} lies across {bus.Id}");
        }
    }
}
