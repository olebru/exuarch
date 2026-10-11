using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class LinkedMicrocodeTests
{
    private static MachineDefinition Default() => MachineDefinition.FromJson(ExampleData.MACHINE);

    private static Machine RunToHalt(Machine c)
    {
        int ticks = 0;
        foreach (var _ in c.Run()) Assert.True(++ticks < 100000, "program did not halt");
        return c;
    }

    [Fact]
    public void DefaultMachineIncludesItsMicrocode()
    {
        var definition = Default();
        Assert.Equal("FTC", definition.Decoder.Microcode.Fetch.Mnemonic);
        Assert.Contains(definition.Decoder.Microcode.Instructions, i => i.Mnemonic == "DWI");
        var c = RunToHalt(new Machine(definition, "\tLAI\t#5\n\tHLT"));
        Assert.Equal(5, c.Device<Register>("rega").Data);
    }

    [Fact]
    public void MicrocodeIsRequired()
    {
        var definition = Default();
        definition.Decoder.Microcode = null;
        var e = Assert.Throws<MachineDefinitionException>(() => new Machine(definition, ""));
        Assert.Contains("decoder.microcode", e.Message);
        Assert.Empty(Machine.ValidateDefinition(definition));
    }

    [Fact]
    public void GivenMicrocodeOverridesTheDefinitionsOwn()
    {
        var microcode = MicrocodeDefinition.FromJson(ExampleData.MICROCODE);
        microcode.FindInstruction("NOP").Mnemonic = "SKIP";
        var c = new Machine(Default(), microcode, "\tSKIP\n\tHLT");
        Assert.Throws<System.ArgumentException>(() => c.DecoderRom.FetchByteCodeFromMnemonic("NOP"));
    }

    [Fact]
    public void DefinitionWithMicrocodeRoundTrips()
    {
        var json = Default().ToJson();
        Assert.Contains("\"microcode\"", json);
        Assert.Equal(json, MachineDefinition.FromJson(json).ToJson());
    }

    [Fact]
    public void RenamingADeviceRewritesItsSignals()
    {
        var definition = Default();
        var before = definition.SignalUsages("regb").Count;
        definition.RenameDevice("regb", "b");

        Assert.Empty(definition.SignalUsages("regb"));
        Assert.Equal(before, definition.SignalUsages("b").Count);
        Assert.Contains("b.output", definition.Decoder.Microcode.FindInstruction("TBA").Steps[0].Signals);
        Assert.Empty(MicrocodeValidator.Validate(definition.Decoder.Microcode, definition));
        RunToHalt(new Machine(definition, ExampleData.HELLO));
    }

    [Fact]
    public void SignalUsagesListEveryStep()
    {
        var usages = Default().SignalUsages("lcd");
        Assert.Equal(new[] { "DCL", "DWA", "DWB", "DWI" }, usages.Select(u => u.Instruction.Mnemonic).Distinct());
        Assert.Contains(usages, u => u.Instruction.Mnemonic == "DWI" && u.Step == 2 && u.Signal == "lcd.load");
    }

    [Fact]
    public void RemovingADeviceKeepsItsSignalsUntilRemoveSignalsOfRemovesThem()
    {
        var keep = Default();
        keep.RemoveDevice("lcd");
        Assert.NotEmpty(keep.SignalUsages("lcd"));
        Assert.Contains(MicrocodeValidator.Validate(keep.Decoder.Microcode, keep), d => d.Message.Contains("unknown device 'lcd'"));

        var clean = Default();
        var count = clean.SignalUsages("lcd").Count;
        clean.RemoveDevice("lcd");
        Assert.Equal(count, clean.RemoveSignalsOf("lcd"));
        Assert.Empty(clean.SignalUsages("lcd"));
        Assert.DoesNotContain(MicrocodeValidator.Validate(clean.Decoder.Microcode, clean), d => d.Severity == DiagnosticSeverity.Error);
    }
}
