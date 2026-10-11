using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ComputerTests
{
    internal static Machine RunToHalt(string src, MachineDefinition definition = null, int maxCycles = 5000)
    {
        var c = new Machine(definition ?? MachineDefinition.FromJson(ExampleData.MACHINE), ExampleData.MICROCODE, src);
        int cycles = 0;
        foreach (var _ in c.Run())
        {
            Assert.True(++cycles < maxCycles, "program did not halt");
        }
        return c;
    }

    [Fact]
    public void AddAndStoreToMemoryBank()
    {
        var c = RunToHalt("\tLAI\t#5\n\tLBI\t#3\n\tADD\n\tSTA\t#200\n\tHLT");
        Assert.Equal(8, c.Device<Register>("rega").Data);
        Assert.Equal(8, c.Device<MMU>("mmu").RamBanks[0].memory[200]);
    }

    [Fact]
    public void CountdownLoopUsesZeroFlag()
    {
        var c = RunToHalt("\tLAI\t#3\n\tLBI\t#1\nloop:\tSUB\n\tJNE\tloop\n\tHLT");
        Assert.Equal(0, c.Device<Register>("rega").Data);
        Assert.True(c.Device<StatusRegister>("regsta").Zero1);
    }

    [Fact]
    public void JumpOnCarryAfterAddWrapsAround()
    {
        var c = RunToHalt("\tLAI\t#65535\n\tLBI\t#1\n\tADD\n\tJC\tcarry\n\tHLT\ncarry:\tLBI\t#42\n\tHLT");
        Assert.Equal(42, c.Device<Register>("regb").Data);
        Assert.True(c.Device<StatusRegister>("regsta").Zero1);
    }

    [Fact]
    public void PushAndPopThroughStack()
    {
        var c = RunToHalt("\tLAI\t#7\n\tPSA\n\tLAI\t#0\n\tPOA\n\tHLT");
        Assert.Equal(7, c.Device<Register>("rega").Data);
        Assert.Equal(0, c.Device<Register>("regsp").Data);
        Assert.Equal(7, c.Device<MMU>("mmu").RamBanks[0].memory[4095]);
    }

    [Fact]
    public void SingleStepDoesNothingOnceHalted()
    {
        var c = RunToHalt("\tHLT");
        var cycles = c.Cycles;
        c.SingleStep();
        Assert.Equal(cycles, c.Cycles);
    }

    [Fact]
    public void ExampleProgramRunsWithoutBusConflicts()
    {
        var c = Machine.CreateExample();
        for (int i = 0; i < 1000; i++) c.SingleStep();
        Assert.Equal(1000, c.Cycles);
    }

    [Fact]
    public void DeviceOrderDoesNotMatter()
    {
        const string src = "\tLAI\t#7\n\tPSA\n\tLAI\t#3\n\tLBI\t#4\n\tADD\n\tSTA\t#100\n\tPOA\n\tHLT";
        var forward = RunToHalt(src);
        var reversedDefinition = MachineDefinition.FromJson(ExampleData.MACHINE);
        reversedDefinition.Devices.Reverse();
        var reversed = RunToHalt(src, reversedDefinition);

        Assert.Equal(forward.Cycles, reversed.Cycles);
        foreach (var register in forward.Devices.OfType<Register>())
        {
            Assert.Equal(register.Data, reversed.Device<Register>(register.ID()).Data);
        }
        Assert.Equal(forward.Device<MMU>("mmu").RamBanks[0].memory, reversed.Device<MMU>("mmu").RamBanks[0].memory);
    }
}
