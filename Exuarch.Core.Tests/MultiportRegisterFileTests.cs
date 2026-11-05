using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class MultiportRegisterFileTests
{
    private const string RegisterFileJson = """
        { "id": "rf", "type": "multiportRegisterFile", "buses": { "a": "left", "b": "right", "w": "main" }, "connections": { "word": "iw" } }
        """;

    private static string MachineJson(string registerFile = RegisterFileJson) => $$"""
        {
          "buses": [ { "id": "main" }, { "id": "left" }, { "id": "right" } ],
          "devices": [
            { "id": "pc", "type": "register", "bus": "main" },
            { "id": "mem", "type": "ram", "bus": "main" },
            { "id": "ir", "type": "instructionRegister", "bus": "main" },
            { "id": "iw", "type": "instructionWord", "bus": "main", "buses": { "fields": "main" } },
            {{registerFile}},
            { "id": "x", "type": "register", "bus": "left" },
            { "id": "y", "type": "register", "bus": "right" },
            { "id": "alu", "type": "alu", "bus": "main", "connections": { "a": "x", "b": "y", "status": "flags" } },
            { "id": "flags", "type": "statusRegister", "bus": "main" },
            { "id": "clk", "type": "clock" }
          ],
          "decoder": {
            "status": "flags",
            "instructionRegister": "ir",
            "microcode": {
              "opcodeBits": 6,
              "fields": [ { "low": 7, "bits": 3 }, { "low": 4, "bits": 3 }, { "low": 1, "bits": 3 } ],
              "fetch": { "mnemonic": "FETCH", "steps": [
                { "signals": ["pc.output", "mem.loadmar"] },
                { "signals": ["mem.output", "ir.load", "iw.load", "pc.inc"] } ] },
              "instructions": [
                { "mnemonic": "HLT", "operands": 0, "steps": [ { "signals": ["clk.disable"] } ] },
                { "mnemonic": "MOVI", "operands": 2, "operandTypes": ["register", "value"], "fields": [0], "steps": [
                  { "signals": ["pc.output", "mem.loadmar"] },
                  { "signals": ["mem.output", "pc.inc", "rf.loadw", "ir.reset"] } ] },
                { "mnemonic": "SUB", "operands": 3, "operandTypes": ["register", "register", "register"], "fields": [0, 1, 2], "steps": [
                  { "signals": ["rf.outputa", "x.load", "rf.outputb", "y.load"] },
                  { "signals": ["alu.sub", "rf.loadw", "ir.reset"] } ] }
              ]
            }
          },
          "halt": "clk",
          "programMemory": "mem"
        }
        """;

    private static Machine Run(string source, string json = null)
    {
        var machine = new Machine(MachineDefinition.FromJson(json ?? MachineJson()), source);
        foreach (var _ in machine.Run().Take(1000)) { }
        Assert.True(machine.IsHalted);
        return machine;
    }

    [Fact]
    public void BothReadPortsDriveInOneTickAndTheWritePortStoresAThird()
    {
        var file = Run("MOVI R1, 50\nMOVI R2, 8\nSUB R5, R1, R2\nHLT").Device<MultiportRegisterFile>("rf");

        Assert.Equal(new[] { 0, 50, 8, 0, 0, 42, 0, 0 }, file.Values);
    }

    [Fact]
    public void TheFieldParametersChooseWhichFieldAddressesEachPort()
    {
        var swapped = RegisterFileJson.Replace("\"connections\"", "\"parameters\": { \"readA\": 2, \"readB\": 1 }, \"connections\"");

        var file = Run("MOVI R1, 50\nMOVI R2, 8\nSUB R5, R1, R2\nHLT", MachineJson(swapped)).Device<MultiportRegisterFile>("rf");

        Assert.Equal(0xFFFF & (8 - 50), file[5]);
    }

    [Fact]
    public void TheWritePortStoresInTheRegisterOfTheWordLatchedBeforeTheTick()
    {
        var machine = new Machine(MachineDefinition.FromJson(MachineJson()), "MOVI R3, 7\nHLT");
        var file = machine.Device<MultiportRegisterFile>("rf");
        machine.SingleStep();
        machine.SingleStep();

        Assert.Equal(3, file.WriteSelected);
        Assert.Equal(0, file[3]);
    }

    [Fact]
    public void RegisterOperandsCountTheMultiportFile()
    {
        var definition = MachineDefinition.FromJson(MachineJson());

        Assert.Equal(8, RegisterFile.CountIn(definition));
        Assert.DoesNotContain(MicrocodeValidator.Validate(definition.Decoder.Microcode, definition), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheFileNeedsAnInstructionWordForItsRegisterNumbers()
    {
        var wrongWord = RegisterFileJson.Replace("\"word\": \"iw\"", "\"word\": \"pc\"");

        var error = Assert.Throws<MachineDefinitionException>(() => new Machine(MachineDefinition.FromJson(MachineJson(wrongWord)), "HLT"));
        Assert.Contains("connection 'word' must be a InstructionWord", error.Message);
    }

    [Fact]
    public void AFieldParameterMustNameOneOfTheFourFields()
    {
        var outOfRange = RegisterFileJson.Replace("\"connections\"", "\"parameters\": { \"write\": 4 }, \"connections\"");

        var error = Assert.Throws<MachineDefinitionException>(() => new Machine(MachineDefinition.FromJson(MachineJson(outOfRange)), "HLT"));
        Assert.Contains("parameter 'write' must be a whole number between 0 and 3", error.Message);
    }
}
