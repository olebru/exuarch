using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class RomTests
{
    private readonly Bus bus = new Bus();
    private readonly Register reg;

    public RomTests()
    {
        reg = new Register("REG", "reg", bus);
        bus.devices.Add(reg);
    }

    private T Attach<T>(T device) where T : IBusDevice
    {
        bus.devices.Add(device);
        return device;
    }

    [Fact]
    public void IncmarMovesOnAfterTheReadInTheSameTick()
    {
        var ram = Attach(new RamModule("RAM", "mem", bus, 16));
        ram.memory[3] = 30;
        ram.memory[4] = 40;
        ram.memoryAddress = 3;

        ram.Enable("output");
        ram.Enable("incmar");
        reg.Enable("load");
        bus.Clk();
        Assert.Equal((30, 4), (reg.Data, ram.memoryAddress));

        ram.Enable("output");
        ram.Enable("incmar");
        reg.Enable("load");
        bus.Clk();
        Assert.Equal((40, 5), (reg.Data, ram.memoryAddress));
    }

    [Fact]
    public void IncmarMovesOnAfterTheWriteInTheSameTick()
    {
        var ram = Attach(new RamModule("RAM", "mem", bus, 16));
        ram.memoryAddress = 7;
        reg.Data = 99;

        reg.Enable("output");
        ram.Enable("load");
        ram.Enable("incmar");
        bus.Clk();

        Assert.Equal((99, 0, 8), (ram.ValueAt(7), ram.ValueAt(8), ram.memoryAddress));
    }

    [Fact]
    public void DecmarMovesBackAfterTheWriteLikeAStackPush()
    {
        var ram = Attach(new RamModule("RAM", "mem", bus, 16));
        ram.memoryAddress = 15;
        reg.Data = 5;

        reg.Enable("output");
        ram.Enable("load");
        ram.Enable("decmar");
        bus.Clk();

        Assert.Equal((5, 14), (ram.ValueAt(15), ram.memoryAddress));
    }

    [Fact]
    public void TheAddressWrapsAtBothEndsOfMemory()
    {
        var ram = Attach(new RamModule("RAM", "mem", bus, 16));
        ram.memoryAddress = 15;
        ram.Enable("incmar");
        bus.Clk();
        Assert.Equal(0, ram.memoryAddress);

        ram.Enable("decmar");
        bus.Clk();
        Assert.Equal(15, ram.memoryAddress);
    }

    [Fact]
    public void IncmarCountsFromAnAddressLoadedInTheSameTick()
    {
        var ram = Attach(new RamModule("RAM", "mem", bus, 256));
        reg.Data = 100;

        reg.Enable("output");
        ram.Enable("loadmar");
        ram.Enable("incmar");
        bus.Clk();

        Assert.Equal(101, ram.memoryAddress);
    }

    [Fact]
    public void TheMmuPassesIncmarToTheSelectedBank()
    {
        var mmu = Attach(new MMU("MMU", "mmu", bus, 2, 16));
        mmu.ChipSelectRegister.Data = 1;
        mmu.RamBanks[1].memory[0] = 11;
        mmu.RamBanks[1].memory[1] = 22;

        mmu.Enable("output");
        mmu.Enable("incmar");
        reg.Enable("load");
        bus.Clk();

        Assert.Equal((11, 1, 0), (reg.Data, mmu.RamBanks[1].memoryAddress, mmu.RamBanks[0].memoryAddress));
    }

    [Fact]
    public void ARomStartsWithItsContentsAndHasNoLoadLine()
    {
        var rom = Attach(new RomModule("ROM", "rom", bus, 16, new[] { "table: .DATA 1, 2, 0x10", "        .STRING \"Hi\"", "        .DATA table" }));

        Assert.Equal(new[] { 1, 2, 16, 'H', 'i', 0, 0 }, Enumerable.Range(0, 7).Select(rom.ValueAt));
        Assert.Equal(new[] { 1, 2, 16, 'H', 'i', 0, 0 }, rom.Contents);
        Assert.DoesNotContain("load", rom.SignalLines());
        Assert.Contains("incmar", rom.SignalLines());
    }

    [Fact]
    public void ARomWithoutContentsReadsAsZero()
    {
        var rom = new RomModule("ROM", "rom", bus, 16);

        Assert.Empty(rom.Contents);
        Assert.False(rom.IsAllocated);
    }

    [Fact]
    public void RomContentsReportInstructionsAndOverflow()
    {
        var instruction = RomContents.Analyze("        LDA 5", 16);
        Assert.Contains(instruction.Errors, e => e.Line == 1 && e.Message.Contains("a ROM holds data, not instructions"));

        var tooLarge = RomContents.Analyze(".DATA 1, 2, 3\n.DATA 4, 5", 4);
        var error = Assert.Single(tooLarge.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("the contents are 5 cells, but the ROM only holds 4", error.Message);
    }

    [Fact]
    public void TablesKeepTheirSignSoTheTextReadsLikeTheFormula()
    {
        Assert.Equal(new[] { 0, 100, 0, -100 }, RomTables.Values(RomTableKind.Sine, 4, 100, 0));
        Assert.Equal(new[] { 110, 10, -90, 10 }, RomTables.Values(RomTableKind.Cosine, 4, 100, 10));
        Assert.Equal(new[] { 0, 1 }, RomTables.Values(RomTableKind.Ramp, 2, 131074, 0));
        Assert.Equal(new[] { 0xFF9C }, RomContents.Analyze(string.Join("\n", RomTables.Lines("t", new[] { -100 })), 4).Cells);
        Assert.Equal(new[] { 0, 25, 50, 75 }, RomTables.Values(RomTableKind.Ramp, 4, 100, 0));
        Assert.Equal(new[] { 0, 1, 4, 9 }, RomTables.Values(RomTableKind.Squares, 4, 4, 0));
    }

    [Fact]
    public void TableLinesHoldEightValuesAndAssembleUnderTheirLabel()
    {
        var lines = RomTables.Lines("ramp", RomTables.Values(RomTableKind.Ramp, 10, 10, 0));

        Assert.Equal(new[] { "ramp:   .DATA 0, 1, 2, 3, 4, 5, 6, 7", "        .DATA 8, 9" }, lines);
        var result = RomContents.Analyze(".DATA 99\n" + string.Join("\n", lines), 16);
        Assert.True(result.Success);
        Assert.Equal(1, result.Labels["ramp"]);
        Assert.Equal(11, result.Cells.Length);
    }

    [Fact]
    public void RomContentsLinesDropTrailingBlankLines()
    {
        Assert.Equal(new[] { ".DATA 1", "", ".DATA 2" }, RomContents.Lines(".DATA 1\r\n\r\n.DATA 2\n\n  \n"));
        Assert.Equal(".DATA 1\n.DATA 2", RomContents.Source(new[] { ".DATA 1", ".DATA 2" }));
    }

    private static MachineDefinition WithRom(List<string> contents, string type = "rom", int? size = null)
    {
        var machine = MachineDefinition.FromJson(ExampleData.MACHINE);
        var rom = new DeviceDefinition { Id = "table", Type = type, Bus = "main", Contents = contents };
        if (size != null) rom.Parameters["size"] = System.Text.Json.JsonSerializer.SerializeToElement(size.Value);
        machine.Devices.Add(rom);
        return machine;
    }

    [Fact]
    public void AMachineBuildsItsRomFromTheDefinition()
    {
        var machine = new Machine(WithRom(new List<string> { ".DATA 7, 8, 9" }, size: 8), ExampleData.MICROCODE, "");

        var rom = machine.Device<RomModule>("table");
        Assert.Equal(8, rom.Size);
        Assert.Equal(new[] { 7, 8, 9 }, Enumerable.Range(0, 3).Select(rom.ValueAt));
    }

    [Fact]
    public void ContentsErrorsAreDefinitionProblems()
    {
        var errors = Machine.ValidateDefinition(WithRom(new List<string> { ".DATA 1", "        ADD" }));
        Assert.Equal("Device 'table': contents line 2: a ROM holds data, not instructions: write .DATA or .STRING instead of 'ADD'.", Assert.Single(errors));

        var full = Machine.ValidateDefinition(WithRom(new List<string> { ".DATA 1, 2, 3" }, size: 2));
        Assert.Contains("Device 'table': contents line 1: the contents are 3 cells, but the ROM only holds 2.", full);
    }

    [Fact]
    public void OnlyARomTakesContents()
    {
        var errors = Machine.ValidateDefinition(WithRom(new List<string> { ".DATA 1" }, type: "ram"));

        Assert.Contains(errors, e => e.StartsWith("Device 'table': a ram has no contents"));
    }

    [Theory]
    [InlineData("Hello from ROM", "Hello from ROM!")]
    [InlineData("Backwards through a buffer", "!MOR morf olleH")]
    public void TheRom16ExamplesWalkMemoryWithTheAddressRegisterAlone(string program, string printed)
    {
        var package = BuiltInPackages.Get("ROM-16");
        var machine = new Machine(package.Machine, package.Programs.Single(p => p.Name == program).Source);
        foreach (var _ in machine.Run().Take(10_000)) { }

        Assert.True(machine.IsHalted);
        Assert.Equal(printed, System.Text.Encoding.Latin1.GetString(machine.Device<CharacterDisplay>("lcd").Cells).TrimEnd());
    }

    [Fact]
    public void TheRom16WaveDrawsOneDotPerColumnFromTheTable()
    {
        var package = BuiltInPackages.Get("ROM-16");
        var machine = new Machine(package.Machine, package.Programs.Single(p => p.Name == "A sine wave from a table").Source);
        foreach (var _ in machine.Run().Take(100_000)) { }

        var screen = machine.Device<Framebuffer>("fb");
        Assert.True(machine.IsHalted);
        Assert.Equal(640, screen.Pixels.Count(p => p != 0));
        Assert.Equal(new[] { 240, 400, 240, 80 }, new[] { 0, 16, 32, 48 }.Select(x => Enumerable.Range(0, 480).First(y => screen.Pixels[y * 640 + x] != 0)));
    }

    [Fact]
    public void ContentsSurviveTheJsonRoundTripAndStayOutWhenUnset()
    {
        var machine = WithRom(new List<string> { "sine: .DATA 0, 50", "      .DATA 100" });
        var json = machine.ToJson();

        Assert.Equal(new[] { "sine: .DATA 0, 50", "      .DATA 100" }, MachineDefinition.FromJson(json).FindDevice("table").Contents);
        Assert.Null(MachineDefinition.FromJson(json).FindDevice("pc").Contents);
        Assert.DoesNotContain("\"contents\": null", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"contents\""));
    }
}
