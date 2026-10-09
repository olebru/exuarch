using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class MicrocodeTests
{
    private static MachineDefinition DefaultMachine() => MachineDefinition.FromJson(ExampleData.MACHINE);
    private static MicrocodeDefinition DefaultMicrocode() => MicrocodeDefinition.FromJson(ExampleData.MICROCODE);

    private static IEnumerable<string> Content(IEnumerable<MicroInstruction> micro)
        => micro.Select(m => $"{m.Mnemonic}:{m.DeviceID}.{m.Function}");

    // Semantics of an instruction: for every flag value, the signals of each step in order.
    private static List<string> Behaviour(InstructionDefinition instruction)
        => Enumerable.Range(0, 16).Select(s => string.Join(" | ", instruction.StepsFor(s).Select(step => string.Join(",", step.Signals)))).ToList();

    private static InstructionDefinition Instruction(string mnemonic, params string[][] steps)
        => new InstructionDefinition { Mnemonic = mnemonic, Steps = steps.Select(s => new MicroStep { Signals = s.ToList() }).ToList() };

    private static MicrocodeDefinition WithInstructions(params InstructionDefinition[] instructions)
    {
        var microcode = DefaultMicrocode();
        microcode.Instructions.AddRange(instructions);
        return microcode;
    }

    private static List<MicrocodeDiagnostic> Validate(MicrocodeDefinition microcode, MachineDefinition machine = null)
        => MicrocodeValidator.Validate(microcode, machine ?? DefaultMachine());

    private static List<MicrocodeDiagnostic> Errors(List<MicrocodeDiagnostic> diagnostics)
        => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static List<MicrocodeDiagnostic> Warnings(List<MicrocodeDiagnostic> diagnostics)
        => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

    [Fact]
    public void TsvImportProducesTheSameRomAsTheLegacyDecoder()
    {
        var legacy = new LegacyDecoderRom(ExampleData.ROMDATA);
        var modern = new DecoderRom(ExampleData.ROMDATA);
        for (int status = 0; status < 16; status++)
        {
            for (int address = 0; address < 256; address++)
            {
                Assert.Equal(Content(legacy.FetchInstruction((byte)status, (byte)address)), Content(modern.FetchInstruction((byte)status, (byte)address)));
            }
        }
        foreach (var mnemonic in modern.Microcode.AllInstructions.Select(i => i.Mnemonic))
        {
            Assert.Equal(legacy.FetchByteCodeFromMnemonic(mnemonic), modern.FetchByteCodeFromMnemonic(mnemonic));
        }
    }

    [Fact]
    public void DefaultJsonMicrocodeBehavesLikeTheLegacyRom()
    {
        var legacy = MicrocodeDefinition.FromTsv(ExampleData.ROMDATA);
        var json = DefaultMicrocode();
        // The JSON keeps the legacy instructions in the same order and adds new ones after them.
        Assert.Equal(legacy.AllInstructions.Select(i => i.Mnemonic), json.AllInstructions.Take(legacy.AllInstructions.Count()).Select(i => i.Mnemonic));
        foreach (var instruction in json.AllInstructions.Take(legacy.AllInstructions.Count()))
        {
            var original = legacy.FindInstruction(instruction.Mnemonic);
            if (instruction.Mnemonic == "CMP")
            {
                // The legacy ROM defined CMP twice; the second copy could never run and is removed.
                Assert.Equal(2, original.Steps.Count);
                original.Steps.RemoveAt(1);
            }
            // The legacy PEA and PEB read whichever MMU bank was selected; they now read the stack in bank 0, like
            // POA and POB (PackageTests.ByocPeeksReadTheStackInBankZero).
            if (instruction.Mnemonic is "PEA" or "PEB") continue;
            // The legacy LPA and LPB read the operand address like LRA and LRB, never following the pointer stored
            // there; they now follow it (PackageTests.ByocPointerLoadsFollowThePointer).
            if (instruction.Mnemonic is "LPA" or "LPB") continue;
            Assert.Equal(Behaviour(original), Behaviour(instruction));
        }
    }

    [Fact]
    public void DefaultMicrocodeIsCleanForTheDefaultMachine()
    {
        Assert.Empty(Validate(DefaultMicrocode()));
        Assert.Empty(Machine.CreateExample().MicrocodeWarnings);
    }

    [Fact]
    public void JsonRoundTripsAndStaysCompact()
    {
        var json = DefaultMicrocode().ToJson();
        Assert.Equal(json, MicrocodeDefinition.FromJson(json).ToJson());
        Assert.Contains("\"signals\": [\"pc.output\", \"mem.loadmar\"]", json);
        Assert.Contains("\"when\": { \"Z\": true }", json);
    }

    [Fact]
    public void ParseAcceptsJsonAndTsv()
    {
        Assert.Equal("FTC", MicrocodeDefinition.Parse(ExampleData.MICROCODE).Fetch.Mnemonic);
        Assert.Equal("FTC", MicrocodeDefinition.Parse(ExampleData.ROMDATA).Fetch.Mnemonic);
    }

    [Theory]
    [InlineData("xxxx", 0, true)]
    [InlineData("xxx1", StatusRegister.ZeroFlag, true)]
    [InlineData("xxx1", 0, false)]
    [InlineData("xx0x", StatusRegister.ZeroFlag, true)]
    [InlineData("xx0x", StatusRegister.CarryFlag, false)]
    [InlineData("1xx0", StatusRegister.NegativeFlag, true)]
    [InlineData("1xx0", StatusRegister.NegativeFlag | StatusRegister.ZeroFlag, false)]
    [InlineData("xxxx1", FlagCondition.InterruptBit, true)]
    [InlineData("xxxx1", StatusRegister.ZeroFlag, false)]
    [InlineData("xxx10", StatusRegister.ZeroFlag, true)]
    [InlineData("xxx10", StatusRegister.ZeroFlag | FlagCondition.InterruptBit, false)]
    public void FlagConditionMatches(string pattern, int status, bool expected)
    {
        var condition = FlagCondition.FromPattern(pattern);
        Assert.Equal(expected, condition == null || condition.Matches(status));
        // Four character patterns leave the interrupt condition out.
        Assert.Equal(pattern.PadRight(5, 'x'), FlagCondition.ToPattern(condition));
    }

    [Fact]
    public void TsvRowWithDifferentConditionThanItsStepIsRejected()
    {
        var tsv = "p\tpc\toutput\tAAA\tx\tx\tx\t1\ns\tmem\tloadmar\tAAA\tx\tx\tx\t0";
        var e = Assert.Throws<FormatException>(() => MicrocodeDefinition.FromTsv(tsv));
        Assert.Contains("line 2", e.Message);
    }

    [Theory]
    [InlineData("rega", "unknown device 'ghost'", "ghost.load")]
    [InlineData("rega", "has no control line 'explode'", "rega.explode")]
    [InlineData("rega", "not a signal", "rega")]
    [InlineData("rega", "not a signal", "rega.load.now")]
    public void SignalsAreCheckedAgainstTheMachine(string _, string expected, string signal)
    {
        var errors = Errors(Validate(WithInstructions(Instruction("BAD", new[] { signal, "regi.reset" }))));
        var error = Assert.Single(errors);
        Assert.Contains(expected, error.Message);
        Assert.Equal("BAD", error.Instruction);
        Assert.Equal(0, error.Step);
        Assert.Equal(signal, error.Signal);
    }

    [Fact]
    public void TwoDriversOnOneBusIsAnError()
    {
        var error = Assert.Single(Errors(Validate(WithInstructions(Instruction("BAD", new[] { "rega.output", "regb.output", "regc.load", "regi.reset" })))));
        Assert.Contains("rega.output and regb.output all drive bus 'main'", error.Message);
        Assert.Equal(0, error.Step);
    }

    [Fact]
    public void DriversOnDifferentBusesDoNotConflict()
    {
        var machine = DefaultMachine();
        machine.Buses.Add(new BusDefinition { Id = "io" });
        machine.FindDevice("regc").Bus = "io";
        machine.Devices.Add(new DeviceDefinition { Id = "out", Type = "register", Bus = "io" });
        var diagnostics = Validate(WithInstructions(Instruction("OK", new[] { "rega.output", "regb.load", "regc.output", "out.load", "regi.reset" })), machine);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReadingAnUndrivenBusIsAWarning()
    {
        var warning = Assert.Single(Warnings(Validate(WithInstructions(Instruction("RDZ", new[] { "rega.load", "regi.reset" })))));
        Assert.Contains("rega.load read bus 'main', but nothing drives it", warning.Message);
    }

    [Fact]
    public void FallingThroughIntoTheNextInstructionIsAWarning()
    {
        var warning = Assert.Single(Warnings(Validate(WithInstructions(Instruction("RUN", new[] { "rega.inc" }, new[] { "rega.inc" })))));
        Assert.Contains("never resets or loads 'regi'", warning.Message);
        Assert.Equal(1, warning.Step);
    }

    [Fact]
    public void StepsAfterReturningToFetchAreAWarning()
    {
        var legacy = MicrocodeDefinition.FromTsv(ExampleData.ROMDATA);
        var warning = Assert.Single(Warnings(Validate(legacy)));
        Assert.Equal("CMP", warning.Instruction);
        Assert.Contains("steps after it never run", warning.Message);
    }

    [Fact]
    public void FlagVariantsAreCheckedSeparately()
    {
        var microcode = DefaultMicrocode();
        var jeq = microcode.FindInstruction("JEQ");
        jeq.Steps.Last().Signals.Remove("regi.reset");
        var warning = Assert.Single(Warnings(Validate(microcode)));
        Assert.StartsWith("when Z=0:", warning.Message);
        Assert.Equal(3, warning.Step);
    }

    [Fact]
    public void MnemonicsMustBeUniqueWordsAndInstructionsNeedSteps()
    {
        var microcode = WithInstructions(
            Instruction("NOP", new[] { "regi.reset" }),
            Instruction("TWO WORDS", new[] { "regi.reset" }),
            new InstructionDefinition { Mnemonic = "EMPTY" });
        var messages = Errors(Validate(microcode)).Select(e => e.Message).ToList();
        Assert.Contains("'NOP' is defined more than once.", messages);
        Assert.Contains("mnemonic must be a single word.", messages);
        Assert.Contains("has no steps.", messages);
    }

    [Fact]
    public void FetchIsRequired()
    {
        var microcode = DefaultMicrocode();
        microcode.Fetch = null;
        Assert.Contains(Errors(Validate(microcode)), e => e.Message.Contains("fetch routine is required"));
    }

    [Fact]
    public void AddressSpaceIsChecked()
    {
        var microcode = WithInstructions(Enumerable.Range(0, DecoderRom.AddressSpace).Select(i => Instruction($"X{i}", new[] { "regi.reset" })).ToArray());
        Assert.Contains(Errors(Validate(microcode)), e => e.Message.Contains("opcodes but only 65536"));
    }

    [Fact]
    public void ControlLineMetadataMatchesTheDevices()
    {
        var registry = DeviceRegistry.CreateDefault();
        var machine = DefaultMachine();
        machine.Buses.Add(new BusDefinition { Id = "io" });
        machine.Devices.Add(new DeviceDefinition { Id = "bridge", Type = "dualPortRegister", Buses = { ["a"] = "main", ["b"] = "io" } });
        // The blitter and the rasterizer draw on a screen on their own video bus.
        machine.Devices.Add(new DeviceDefinition { Id = "screen", Type = "framebuffer", Bus = "io" });
        machine.Devices.Add(new DeviceDefinition { Id = "pages", Type = "doubleFramebuffer", Bus = "io" });
        machine.Devices.Add(new DeviceDefinition { Id = "blit", Type = "blitter", Buses = { ["host"] = "main", ["video"] = "io" }, Connections = { ["screen"] = "screen" } });
        machine.Devices.Add(new DeviceDefinition { Id = "tick", Type = "timer", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "clock", Type = "rtc", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "pic", Type = "interruptController", Bus = "main", Connections = { ["irq0"] = "tick", ["irq1"] = "blit", ["irq2"] = "clock" } });
        machine.Buses.Add(new BusDefinition { Id = "lb" });
        machine.Devices.Add(new DeviceDefinition { Id = "lmem", Type = "ram", Bus = "lb" });
        machine.Devices.Add(new DeviceDefinition { Id = "table", Type = "rom", Bus = "lb", Contents = new List<string> { ".DATA 1, 2, 3" } });
        machine.Devices.Add(new DeviceDefinition { Id = "zb", Type = "depthBuffer", Bus = "io" });
        machine.Devices.Add(new DeviceDefinition { Id = "gmac", Type = "mac", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "rf", Type = "registerFile", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "rast", Type = "rasterizer", Buses = { ["host"] = "main", ["list"] = "lb", ["video"] = "io" }, Connections = { ["screen"] = "screen", ["depth"] = "zb", ["memory"] = "lmem" } });
        var built = new Machine(machine, ExampleData.MICROCODE, "");
        Assert.Equal(registry.Types.OrderBy(t => t), machine.Devices.Select(d => d.Type).Distinct().OrderBy(t => t));
        foreach (var device in machine.Devices)
        {
            Assert.Equal(built.Device(device.Id).SignalLines().OrderBy(l => l), registry.Info(device.Type).ControlLines.Select(l => l.Name).OrderBy(l => l));
        }
    }

    [Fact]
    public void UndescribedDeviceTypesFallBackToTheBuiltDevice()
    {
        var registry = DeviceRegistry.CreateDefault();
        registry.Register("plain", c => new Register(c.Name, c.Id, c.Bus()));
        var machine = DefaultMachine();
        machine.Devices.Add(new DeviceDefinition { Id = "plain", Type = "plain", Bus = "main" });
        var microcode = WithInstructions(Instruction("PLN", new[] { "plain.inc", "plain.bogus", "regi.reset" }));

        var withoutBuilt = MicrocodeValidator.Validate(microcode, machine, registry);
        Assert.Empty(Errors(withoutBuilt));
        var e = Assert.Throws<MachineDefinitionException>(() => new Machine(machine, microcode, "", registry));
        Assert.Contains("has no control line 'bogus'", e.Message);
    }

    [Fact]
    public void AssemblerChecksDeclaredOperandCounts()
    {
        var rom = new DecoderRom(DefaultMicrocode());
        var assembler = new Assembler(rom);
        var e = Assert.Throws<FormatException>(() => assembler.Assemble("\tLAI"));
        Assert.Contains("LAI takes 1 operand, found 0", e.Message);
        e = Assert.Throws<FormatException>(() => assembler.Assemble("\tNOP\t#1"));
        Assert.Contains("NOP takes 0 operands, found 1", e.Message);
        Assert.Equal(3, assembler.Assemble("\tLAI\t#1\n\tNOP").Length);
    }

    [Fact]
    public void DiagnosticsDescribeTheirLocation()
    {
        var error = Assert.Single(Errors(Validate(WithInstructions(Instruction("BAD", new[] { "regi.reset" }, new[] { "ghost.load" })))));
        Assert.Equal("Microcode BAD step 2: unknown device 'ghost'.", error.ToString());
    }
}
