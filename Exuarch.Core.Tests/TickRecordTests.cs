using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class TickRecordTests
{
    private static Machine Default(string src = null) =>
        new Machine(MachineDefinition.FromJson(ExampleData.MACHINE), ExampleData.MICROCODE, src ?? ExampleData.SRC);

    [Fact]
    public void FetchTicksRecordBusTransfersAndTheFetchedAddress()
    {
        var c = Default();
        c.SingleStep();
        var first = c.LastTick;
        Assert.Equal(1, first.Cycle);
        Assert.Equal("FTC", first.Instruction);
        Assert.Equal(0, first.StepIndex);
        Assert.Equal(new[] { "pc.output", "mem.loadmar" }, first.Signals);
        var transfer = Assert.Single(first.Transfers);
        Assert.Equal(("main", "pc", 0), (transfer.Bus, transfer.Driver, transfer.Value));
        Assert.Equal(new[] { "mem" }, transfer.Readers);
        Assert.Null(first.FetchedFromAddress);

        c.SingleStep();
        var second = c.LastTick;
        Assert.Equal(1, second.StepIndex);
        Assert.Equal("mem", second.Transfers[0].Driver);
        Assert.Equal(c.DecoderRom.FetchByteCodeFromMnemonic("LAI"), second.Transfers[0].Value);
        Assert.Equal(new[] { "regi" }, second.Transfers[0].Readers);
        Assert.Equal(0, second.FetchedFromAddress);
        Assert.Equal(0, c.CurrentInstructionAddress);
    }

    [Fact]
    public void StepInstructionExecutesOneInstructionAndFetchesTheNext()
    {
        var c = Default();
        Assert.Equal(2, c.StepInstruction());
        Assert.Equal(0, c.CurrentInstructionAddress);
        Assert.Equal("LAI", c.NextStep.Value.Instruction.Mnemonic);

        c.StepInstruction();
        Assert.Equal(15, c.Device<Register>("rega").Data);
        Assert.Equal(2, c.CurrentInstructionAddress);
        Assert.Contains(c.History, t => t.Changes.Any(ch => ch.Device == "rega" && ch.Before == 0 && ch.After == 15));
    }

    [Fact]
    public void StackPushIsRecordedAsAMemoryWrite()
    {
        var c = Default();
        c.StepInstruction();
        c.StepInstruction();
        c.StepInstruction();
        var write = Assert.Single(c.History.SelectMany(t => t.Writes));
        Assert.Equal(("mmu", 0, 4095, 15), (write.Device, write.Bank, write.Address, write.Value));
        Assert.Contains(c.History, t => t.Changes.Any(ch => ch.Device == "regsp" && ch.After == 65535));
    }

    [Fact]
    public void FloatingBusHasNoDriver()
    {
        var c = Default("\tRST\n\tHLT");
        c.StepInstruction();
        c.SingleStep();
        Assert.Equal(new[] { "regsta.reset", "regi.reset", "pc.inc" }, c.LastTick.Signals);
        Assert.Null(c.LastTick.Transfers[0].Driver);
    }

    [Fact]
    public void HistoryIsCapped()
    {
        var c = Default();
        for (int i = 0; i < Machine.HistoryLimit + 20; i++) c.SingleStep();
        Assert.Equal(Machine.HistoryLimit, c.History.Count);
        Assert.Equal(c.Cycles, c.History.Last().Cycle);
    }

    [Fact]
    public void ListingMapsSourceLinesToAddresses()
    {
        var c = Default();
        var listing = c.Assembler.Listing;
        Assert.Equal(11, listing.Count);
        Assert.Equal((0, "LAI", 2), (listing[0].Address, listing[0].Mnemonic, listing[0].Cells.Length));
        var loop = listing.Single(l => l.Label == "loop");
        Assert.Equal(9, loop.Address);
        var letter = listing.Single(l => l.Label == "letter");
        Assert.False(letter.IsInstruction);
        Assert.Equal(new[] { 65 }, letter.Cells);
    }

    [Fact]
    public void LocateFindsTheStepForTheFlags()
    {
        var rom = new DecoderRom(MicrocodeDefinition.FromJson(ExampleData.MICROCODE));
        var jeq = rom.FetchByteCodeFromMnemonic("JEQ");
        var taken = rom.Locate(StatusRegister.ZeroFlag, (byte)(jeq + 1)).Value;
        Assert.Equal("JEQ", taken.Instruction.Mnemonic);
        Assert.Equal(1, taken.Instruction.Steps.IndexOf(taken.Step));
        var notTaken = rom.Locate(0, (byte)(jeq + 1)).Value;
        Assert.Equal(3, notTaken.Instruction.Steps.IndexOf(notTaken.Step));
        Assert.Null(rom.Locate(0, (byte)(jeq + 2)).Value.Step);
    }

    [Theory]
    [InlineData("DSP-16", "Lightning at the top of the set")]
    [InlineData("TURBO-16", "A spinning cube")]
    public void RunningWithoutHistoryAllocatesNothingPerTick(string package, string program)
    {
        var p = BuiltInPackages.Get(package);
        var c = new Machine(p.Machine, p.Program(program).Source) { RecordHistory = false };
        for (int i = 0; i < 200_000; i++) c.SingleStep();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200_000; i++) c.SingleStep();
        long perTick = (System.GC.GetAllocatedBytesForCurrentThread() - before) / 200_000;
        Assert.True(perTick == 0, $"{perTick} bytes a tick");
        Assert.Equal(c.Cycles, c.LastTick.Cycle);
    }
}
