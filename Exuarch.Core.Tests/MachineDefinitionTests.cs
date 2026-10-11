using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class MachineDefinitionTests
{
    private static string Row(string clk, string device, string function, string mnemonic)
        => $"{clk}\t{device}\t{function}\t{mnemonic}\tx\tx\tx\tx";

    private const string MinimalJson = """
        {
          "name": "mini",
          "buses": [ { "id": "main" } ],
          "devices": [
            { "id": "pc", "type": "register", "bus": "main" },
            { "id": "mem", "type": "ram", "bus": "main" },
            { "id": "ir", "type": "instructionRegister", "bus": "main" },
            { "id": "st", "type": "statusRegister", "bus": "main" },
            { "id": "clk", "type": "clock" }
          ],
          "decoder": { "status": "st", "instructionRegister": "ir" },
          "halt": "clk",
          "programMemory": "mem"
        }
        """;

    private static readonly string FetchAndHalt = string.Join("\n",
        Row("p", "pc", "output", "FTC"),
        Row("s", "mem", "loadmar", "FTC"),
        Row("p", "mem", "output", "FTC"),
        Row("s", "ir", "load", "FTC"),
        Row("s", "pc", "inc", "FTC"),
        Row("p", "clk", "disable", "HLT"));

    private static MachineDefinitionException Fails(string json, string microcode = null, DeviceRegistry registry = null)
    {
        return Assert.Throws<MachineDefinitionException>(() => Machine.FromJson(json, microcode ?? FetchAndHalt, "", registry));
    }

    [Fact]
    public void TheOldInstructionKeyStillLoadsAndIsWrittenAsInstructionRegister()
    {
        var old = MinimalJson.Replace("\"instructionRegister\": \"ir\"", "\"instruction\": \"ir\"");
        Assert.Contains("\"instruction\": \"ir\"", old);
        var definition = MachineDefinition.FromJson(old);
        Assert.Equal("ir", definition.Decoder.InstructionRegister);
        var json = definition.ToJson();
        Assert.Contains("\"instructionRegister\": \"ir\"", json);
        Assert.DoesNotContain("\"instruction\":", json);
        Assert.IsType<InstructionRegister>(Machine.FromJson(old, FetchAndHalt, "\tHLT").Device("ir"));
    }

    [Fact]
    public void ExampleMachineLoads()
    {
        var c = Machine.CreateExample();
        Assert.Equal("BYOC-16", c.Definition.Name);
        Assert.Equal(new[] { "regi", "pc", "regsp", "rega", "regb", "regc", "regs", "alu", "regsta", "mem", "mmu", "clk", "lcd", "fb", "keys" },
            c.Devices.Select(d => d.ID()));
        Assert.Equal(0, c.Device<Register>("regsp").Data);
        Assert.Equal("REGSP", c.Device<Register>("regsp").DisplayName());
    }

    [Fact]
    public void DefinitionRoundTripsThroughJson()
    {
        var definition = MachineDefinition.FromJson(ExampleData.MACHINE);
        var again = MachineDefinition.FromJson(definition.ToJson());
        Assert.Equal(definition.ToJson(), again.ToJson());
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        var json = MinimalJson.Replace("\"name\": \"mini\",", "// a comment\n\"name\": \"mini\",").Replace("\"programMemory\": \"mem\"", "\"programMemory\": \"mem\",");
        Assert.Equal("mini", Machine.FromJson(json, FetchAndHalt, "").Definition.Name);
    }

    [Fact]
    public void MinimalMachineRunsToHalt()
    {
        var c = Machine.FromJson(MinimalJson, FetchAndHalt, "\tHLT");
        Assert.Equal(3, c.Run().Count());
        Assert.True(c.IsHalted);
    }

    [Fact]
    public void MisspelledPropertyIsRejected()
    {
        var e = Fails(MinimalJson.Replace("\"halt\"", "\"hlat\""));
        Assert.Contains("hlat", e.Message);
    }

    [Fact]
    public void InvalidJsonIsReported()
    {
        Assert.Contains("not valid JSON", Fails("{ \"name\": ").Message);
    }

    [Fact]
    public void AllStructuralErrorsAreReportedTogether()
    {
        var json = """
            {
              "buses": [ { "id": "main" } ],
              "devices": [
                { "id": "a", "type": "register", "bus": "nowhere" },
                { "id": "a", "type": "register", "bus": "main" },
                { "id": "x", "type": "flux-capacitor", "bus": "main" },
                { "id": "alu", "type": "alu", "bus": "main", "connections": { "a": "ghost" } }
              ],
              "decoder": { "status": "missing" },
              "halt": "nope"
            }
            """;
        var errors = Fails(json).Errors;
        Assert.Contains(errors, e => e.Contains("unknown bus 'nowhere'"));
        Assert.Contains(errors, e => e.Contains("'a' is defined more than once"));
        Assert.Contains(errors, e => e.Contains("unknown type 'flux-capacitor'"));
        Assert.Contains(errors, e => e.Contains("unknown device 'ghost'"));
        Assert.Contains(errors, e => e.Contains("\"decoder.status\" refers to unknown device 'missing'"));
        Assert.Contains(errors, e => e.Contains("\"decoder.instructionRegister\" is required"));
        Assert.Contains(errors, e => e.Contains("\"halt\" refers to unknown device 'nope'"));
    }

    [Fact]
    public void ConnectionOfWrongDeviceTypeIsRejected()
    {
        var json = MinimalJson.Replace("{ \"id\": \"clk\", \"type\": \"clock\" }",
            "{ \"id\": \"clk\", \"type\": \"clock\" }, { \"id\": \"alu\", \"type\": \"alu\", \"bus\": \"main\", \"connections\": { \"a\": \"clk\", \"b\": \"st\", \"status\": \"st\" } }");
        var e = Fails(json);
        Assert.Contains("connection 'a' must be a Register", e.Message);
    }

    [Fact]
    public void MissingBusForDeviceIsRejected()
    {
        var e = Fails(MinimalJson.Replace("{ \"id\": \"st\", \"type\": \"statusRegister\", \"bus\": \"main\" }", "{ \"id\": \"st\", \"type\": \"statusRegister\" }"));
        Assert.Contains("Device 'st'", e.Message);
        Assert.Contains("needs a bus connection", e.Message);
    }

    [Fact]
    public void DecoderMustNameRegisters()
    {
        var e = Fails(MinimalJson.Replace("\"status\": \"st\"", "\"status\": \"clk\""));
        Assert.Contains("\"decoder.status\" must name a Register", e.Message);
    }

    [Fact]
    public void ConnectionCycleIsReported()
    {
        var registry = DeviceRegistry.CreateDefault();
        registry.Register("loop", c => { c.Connection<IBusDevice>("next"); return new Clock(c.Name, c.Id); });
        var json = MinimalJson.Replace("{ \"id\": \"clk\", \"type\": \"clock\" }",
            "{ \"id\": \"clk\", \"type\": \"clock\" }, { \"id\": \"l1\", \"type\": \"loop\", \"connections\": { \"next\": \"l2\" } }, { \"id\": \"l2\", \"type\": \"loop\", \"connections\": { \"next\": \"l1\" } }");
        Assert.Contains("cycle: l1 -> l2 -> l1", Fails(json, registry: registry).Message);
    }

    [Fact]
    public void MicrocodeIsCheckedAgainstDevices()
    {
        var microcode = FetchAndHalt + "\n" + Row("p", "ghost", "load", "BAD") + "\n" + Row("p", "pc", "explode", "BAD");
        var errors = Fails(MinimalJson, microcode).Errors;
        Assert.Contains(errors, e => e.Contains("unknown device 'ghost'"));
        Assert.Contains(errors, e => e.Contains("device 'pc' has no control line 'explode'"));
    }

    [Fact]
    public void ProgramNeedsProgramMemory()
    {
        var json = MinimalJson.Replace(",\n  \"programMemory\": \"mem\"", "").Replace(",\r\n  \"programMemory\": \"mem\"", "");
        var e = Assert.Throws<MachineDefinitionException>(() => Machine.FromJson(json, FetchAndHalt, "\tHLT"));
        Assert.Contains("programMemory", e.Message);
    }

    [Fact]
    public void CustomDeviceTypesCanBeRegistered()
    {
        var registry = DeviceRegistry.CreateDefault();
        registry.Register("counter", c => new CountingRegister(c.Name, c.Id, c.Bus()));
        var json = MinimalJson.Replace("{ \"id\": \"pc\", \"type\": \"register\"", "{ \"id\": \"pc\", \"type\": \"counter\"");
        var c = Machine.FromJson(json, FetchAndHalt, "\tHLT", registry);
        Assert.IsType<CountingRegister>(c.Device("pc"));
    }

    [Fact]
    public void ValueCrossesBusesThroughDualPortRegister()
    {
        var json = """
            {
              "buses": [ { "id": "main" }, { "id": "io" } ],
              "devices": [
                { "id": "pc", "type": "register", "bus": "main" },
                { "id": "mem", "type": "ram", "bus": "main" },
                { "id": "ir", "type": "instructionRegister", "bus": "main" },
                { "id": "st", "type": "statusRegister", "bus": "main" },
                { "id": "rega", "type": "register", "bus": "main" },
                { "id": "bridge", "type": "dualPortRegister", "buses": { "a": "main", "b": "io" } },
                { "id": "out", "type": "register", "bus": "io" },
                { "id": "clk", "type": "clock" }
              ],
              "decoder": { "status": "st", "instructionRegister": "ir" },
              "halt": "clk",
              "programMemory": "mem"
            }
            """;
        var microcode = FetchAndHalt + "\n" + string.Join("\n",
            Row("p", "rega", "output", "OUT"),
            Row("s", "bridge", "loada", "OUT"),
            Row("p", "bridge", "outputb", "OUT"),
            Row("s", "out", "load", "OUT"),
            Row("s", "ir", "reset", "OUT"));

        var c = Machine.FromJson(json, microcode, "\tOUT\n\tHLT");
        c.Device<Register>("rega").Data = 42;
        foreach (var _ in c.Run().Take(100)) { }

        Assert.True(c.IsHalted);
        Assert.Equal(42, c.Device<Register>("out").Data);
        Assert.Contains(c.Device("bridge"), c.Buses["main"].devices);
        Assert.Contains(c.Device("bridge"), c.Buses["io"].devices);
        Assert.DoesNotContain(c.Device("out"), c.Buses["main"].devices);
    }

    [Fact]
    public void BusConflictInMicrocodeIsRejectedAtLoad()
    {
        var microcode = FetchAndHalt + "\n" + Row("p", "pc", "output", "BAD") + "\n" + Row("s", "mem", "output", "BAD");
        var e = Fails(MinimalJson, microcode);
        Assert.Contains("bus 'main'", e.Message);
        Assert.Contains("pc.output", e.Message);
        Assert.Contains("mem.output", e.Message);
    }

    [Fact]
    public void TheDecoderNeedsAnInstructionRegister()
    {
        var json = MinimalJson.Replace("{ \"id\": \"ir\", \"type\": \"instructionRegister\"", "{ \"id\": \"ir\", \"type\": \"register\"");
        var e = Fails(json, FetchAndHalt);
        Assert.Contains("\"decoder.instructionRegister\" must name an InstructionRegister", e.Message);
    }

    private class CountingRegister : Register
    {
        public CountingRegister(string name, string id, Bus bus) : base(name, id, bus) { }
    }

    [Fact]
    public void AnOldInitialValueIsDroppedWhenAFileOpens()
    {
        var json = """
            {
              "buses": [ { "id": "main" } ],
              "devices": [
                { "id": "sp", "type": "register", "bus": "main", "parameters": { "initialValue": 4096 } },
                { "id": "bridge", "type": "dualPortRegister", "buses": { "a": "main", "b": "main" }, "parameters": { "initialValue": 7 } }
              ]
            }
            """;
        var definition = MachineDefinition.FromJson(json);
        Assert.All(definition.Devices, d => Assert.Empty(d.Parameters));
        Assert.DoesNotContain("initialValue", definition.ToJson());
        var package = MachinePackage.FromJson("{ \"name\": \"Old\", \"machine\": " + json + " }");
        Assert.All(package.Machine.Devices, d => Assert.Empty(d.Parameters));
    }

    [Fact]
    public void TheDecoderHasAPlaceOnTheCanvas()
    {
        var machine = MachineTemplates.Minimal("Mine").Machine;
        var layout = machine.Decoder.Layout;
        Assert.NotNull(layout);
        var top = machine.Devices.Min(d => d.Layout.Y);
        Assert.Equal(top, layout.Y);
        Assert.All(machine.Devices.Where(d => d.Layout.Y == top), d => Assert.True(layout.X >= d.Layout.X + MachineDefinitionEditing.LayoutCardWidth));

        layout.X = 999;
        var back = MachineDefinition.FromJson(machine.ToJson());
        Assert.Equal(999, back.Decoder.Layout.X);
        Assert.Empty(Machine.ValidateDefinition(back, DeviceRegistry.CreateDefault()));

        back.Decoder.Layout = null;
        back.EnsureLayout();
        Assert.NotNull(back.Decoder.Layout);
        var empty = MachineTemplates.Empty("Blank").Machine;
        empty.EnsureLayout();
        Assert.Null(empty.Decoder);
    }

    [Fact]
    public void ARegisterHasNoParameters()
    {
        var registry = DeviceRegistry.CreateDefault();
        Assert.Empty(registry.Info("register").Parameters);
        Assert.Empty(registry.Info("dualPortRegister").Parameters);
    }
}
