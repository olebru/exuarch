using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class BlazeTests
{
    private static MachinePackage Blaze() => BuiltInPackages.Get("BLAZE-16");

    private static Machine Start(MachinePackage package, string program) =>
        new Machine(package.Machine, package.Programs.Single(p => p.Name == program).Source) { RecordHistory = false };

    private static Machine RunToHalt(MachinePackage package, string program, int limit)
    {
        var machine = Start(package, program);
        foreach (var _ in machine.Run().Take(limit)) { }
        Assert.True(machine.IsHalted, $"{package.Name} / {program} did not halt in {limit} ticks");
        return machine;
    }

    [Fact]
    public void TheSmallestPictureIsDsp16sPixelForPixelInAnEighthOfTheTicks()
    {
        const string program = "Mandelbrot, 80 x 60";
        var dsp = RunToHalt(BuiltInPackages.Get("DSP-16"), program, 9_000_000);
        var blaze = RunToHalt(Blaze(), program, 1_100_000);

        Assert.Equal(dsp.Device<Framebuffer>("fb").Pixels, blaze.Device<Framebuffer>("fb").Pixels);
        Assert.Equal(1_005_724, blaze.Cycles);
        Assert.True(blaze.Cycles * 8 < dsp.Cycles);
    }

    [Fact]
    public void EveryStepOfMiterTakesSeventeenTicks()
    {
        var package = Blaze();
        var source = "MOVI R1, 16385\nSLIM R1\nMOVI R1, 5\nSMAX R1\nMOVI R1, 0\nSCX R1\nSCY R1\nMPOINT\nMITER\nHLT";
        var machine = new Machine(package.Machine, source);
        for (int i = 0; i < 9; i++) machine.StepInstruction();
        int before = machine.Cycles;
        machine.StepInstruction();

        Assert.Equal(5 * 17, machine.Cycles - before);
        Assert.Equal(0, machine.Device<Register>("n").Data);
        Assert.Equal(0, machine.Device<Register>("k").Data);
    }

    [Fact]
    public void AnEscapingPointLeavesTheStepsItHadLeft()
    {
        var package = Blaze();
        var source = "MOVI R1, 16385\nSLIM R1\nMOVI R1, 32\nSMAX R1\nMOVI R1, 8192\nSCX R1\nMOVI R1, 0\nSCY R1\nMPOINT\nMITER\nHLT";
        var machine = new Machine(package.Machine, source);
        foreach (var _ in machine.Run().Take(10_000)) { }

        Assert.True(machine.IsHalted);
        Assert.Equal(31, machine.Device<Register>("k").Data);
        Assert.Equal(0, machine.Device<Register>("n").Data);
    }

    [Fact]
    public void TheColourCycleTakesTheSameTicksEveryFrameAndTurnsTheColours()
    {
        var machine = Start(Blaze(), "Colour cycling, 80 x 60");
        int frame = machine.Assembler.labelLUT["frame"];
        var starts = new List<int>();
        var pictures = new List<ushort[]>();
        while (starts.Count < 3 && machine.Cycles < 2_000_000)
        {
            machine.SingleStep();
            if (machine.LastTick.FetchedFromAddress != frame) continue;
            starts.Add(machine.Cycles);
            pictures.Add((ushort[])machine.Device<Framebuffer>("fb").Pixels.Clone());
        }

        Assert.Equal(new[] { 96_027, 96_027 }, starts.Zip(starts.Skip(1), (a, b) => b - a));
        Assert.NotEqual(pictures[1], pictures[2]);
        var screen = pictures[2];
        Assert.Equal(0, screen[100 * Framebuffer.Width + 100]);
        Assert.NotEqual(0, screen[120 * Framebuffer.Width + 160]);
    }

    [Fact]
    public void FieldsReachTheDataBusWhileTheWordComesFromTheProgramBus()
    {
        var machine = Start(Blaze(), "Mandelbrot, 80 x 60");
        var word = machine.Device<InstructionWord>("iw");

        Assert.Contains(machine.Buses["ibus"].devices, d => d == word);
        Assert.Contains(machine.Buses["dbus"].devices, d => d == word);
        Assert.Empty(MicrocodeValidator.Validate(machine.Definition.Decoder.Microcode, machine.Definition).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
