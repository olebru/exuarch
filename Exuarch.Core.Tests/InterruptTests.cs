using System;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class InterruptTests
{
    private class Source : IInterruptSource
    {
        public bool Request;
        public bool TakeInterruptRequest() { var taken = Request; Request = false; return taken; }
    }

    [Fact]
    public void TheControllerCollectsMasksAndAcknowledges()
    {
        var bus = new Bus();
        var cpu = new Register("CPU", "cpu", bus);
        var a = new Source();
        var b = new Source();
        var pic = new InterruptController("PIC", "pic", bus, new IInterruptSource[] { a, null, b });
        void Tick(params (IBusDevice Device, string Line)[] lines)
        {
            foreach (var (device, line) in lines) device.Enable(line);
            Clocking.Tick(new[] { bus }, new IBusDevice[] { cpu, pic });
        }

        a.Request = true;
        Tick();
        Assert.Equal(1, pic.Pending);
        Assert.False(pic.Requesting);
        Tick((pic, "enable"));
        Assert.True(pic.Requesting);

        cpu.Data = 0b100;
        Tick((cpu, "output"), (pic, "loadmask"));
        Assert.False(pic.Requesting);
        b.Request = true;
        Tick();
        Assert.Equal(0b101, pic.Pending);
        Assert.True(pic.Requesting);

        Tick((pic, "output"), (cpu, "load"));
        Assert.Equal(0b100, cpu.Data);
        Tick((cpu, "output"), (pic, "ack"));
        Assert.Equal(0b001, pic.Pending);
        Assert.False(pic.Requesting);
        Tick((pic, "disable"));
        Assert.False(pic.Enabled);
        Assert.Throws<ArgumentException>(() => new InterruptController("P", "p", bus, new IInterruptSource[5]));
    }

    [Fact]
    public void TheTimerRaisesAnInterruptEveryPeriod()
    {
        var bus = new Bus();
        var cpu = new Register("CPU", "cpu", bus);
        var timer = new TickTimer("T", "t", bus, 5);
        void Tick(params string[] lines)
        {
            foreach (var line in lines) timer.Enable(line);
            Clocking.Tick(new[] { bus }, new IBusDevice[] { cpu, timer });
        }
        for (int i = 0; i < 20; i++) Tick();
        Assert.False(timer.TakeInterruptRequest());
        Tick("start");
        int requests = 0;
        for (int i = 0; i < 25; i++) { Tick(); if (timer.TakeInterruptRequest()) requests++; }
        Assert.Equal(5, requests);
        Tick("stop");
        for (int i = 0; i < 20; i++) Tick();
        Assert.False(timer.TakeInterruptRequest());
        cpu.Data = 3;
        cpu.Enable("output");
        Tick("loadperiod");
        Assert.Equal(3, timer.Period);
    }

    [Fact]
    public void KeysRaiseAnInterruptOncePerPressAndTheBlitterWhenDone()
    {
        var keys = new Keypad("K", "k", new Bus());
        keys.Press(Keypad.Keys.Left);
        keys.Press(Keypad.Keys.Left);
        Assert.True(keys.TakeInterruptRequest());
        Assert.False(keys.TakeInterruptRequest());
        keys.Release(Keypad.Keys.Left);
        keys.Press(Keypad.Keys.Left);
        Assert.True(keys.TakeInterruptRequest());

        var host = new Bus("host");
        var video = new Bus("video");
        var screen = new Framebuffer("FB", "fb", video);
        var blit = new Blitter("B", "blit", host, video, screen);
        var cpu = new Register("CPU", "cpu", host);
        foreach (var (line, value) in new[] { ("loadw", 2), ("loadh", 1) })
        {
            cpu.Data = value; cpu.Enable("output"); blit.Enable(line);
            Clocking.Tick(new[] { host, video }, new IBusDevice[] { cpu, blit, screen });
        }
        blit.Enable("start");
        Clocking.Tick(new[] { host, video }, new IBusDevice[] { cpu, blit, screen });
        Assert.False(blit.TakeInterruptRequest());
        while (blit.Busy) Clocking.Tick(new[] { host, video }, new IBusDevice[] { cpu, blit, screen });
        Assert.True(blit.TakeInterruptRequest());
        Assert.False(blit.TakeInterruptRequest());
    }

    private static MachinePackage Package() => BuiltInPackages.Get("IRQ-16");

    [Fact]
    public void TheDecoderNamesTheControllerAndMicrocodeCanTestI()
    {
        var machine = Package().Machine;
        Assert.Equal("pic", machine.Decoder.Interrupts);
        var fetch = machine.Decoder.Microcode.Fetch;
        Assert.Equal(2, fetch.StepsFor(0).Count);
        Assert.Equal(6, fetch.StepsFor(FlagCondition.InterruptBit).Count);
        Assert.Empty(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine));
        Assert.Contains("\"I\": true", machine.ToJson());
    }

    [Fact]
    public void TheRequestIsSampledOnlyWhenAnInstructionStarts()
    {
        var c = new Machine(Package().Machine, "\tSETV\thandler\n\tEI\n\tLDA\t100\n\tLDA\t101\n\tHLT\nhandler:\tHLT") { RecordHistory = true };
        var pic = c.Interrupts;
        while (!pic.Enabled) c.SingleStep();
        c.SingleStep();
        c.Device<Keypad>("keys").Press(Keypad.Keys.Up);
        while (!c.IsHalted) c.SingleStep();
        var instructions = c.History.Where(t => t.StepIndex == 0).Select(t => t.Instruction).ToList();
        Assert.Contains("LDA", instructions);
        Assert.Equal(1, instructions.Count(i => i == "LDA"));
        const int SpAfterPushingPcAndFlags = 65534;
        Assert.Equal(SpAfterPushingPcAndFlags, c.Device<Register>("sp").Data);
    }

    [Fact]
    public void WithoutAControllerIIsAlwaysZero()
    {
        var c = new Machine(BuiltInPackages.Get("COPRO-16").Machine, "\tNOP\n\tHLT");
        Assert.Null(c.Interrupts);
        while (!c.IsHalted) { c.SingleStep(); Assert.Equal(0, c.DecoderStatus & FlagCondition.InterruptBit); }
    }

    [Fact]
    public void TheControllerMustBeAnInterruptController()
    {
        var definition = Package().Machine;
        definition.Decoder.Interrupts = "tick";
        var e = Assert.Throws<MachineDefinitionException>(() => new Machine(definition, ""));
        Assert.Contains("\"decoder.interrupts\" must name an InterruptController", e.Message);
    }

    private static (Machine Machine, CharacterDisplay Lcd) Demo()
    {
        var c = new Machine(Package().Machine, Package().Program("Three things at once").Source) { RecordHistory = false };
        return (c, c.Device<CharacterDisplay>("lcd"));
    }

    [Fact]
    public void TheDemoPaintsTheBoardFromInterruptsWhileTheMainLoopCounts()
    {
        var (c, lcd) = Demo();
        var blit = c.Device<Blitter>("blit");
        for (int i = 0; i < 400_000 && blit.JobsDone < 48; i++) c.SingleStep();
        Assert.Equal(48, blit.JobsDone);
        const int TicksToHandleTheLastSquaresInterrupt = 30_000;
        for (int i = 0; i < TicksToHandleTheLastSquaresInterrupt; i++) c.SingleStep();
        var screen = c.Device<Framebuffer>("fb");
        for (int row = 0; row < 6; row++)
            for (int column = 0; column < 8; column++)
                Assert.Equal((row + column) % 2 == 0 ? 0xFFFF : 0, screen.Pixels[(row * 80 + 40) * Framebuffer.Width + column * 80 + 40]);
        Assert.Contains("squares 48", lcd.Line(0));
        Assert.StartsWith("main loop ", lcd.Line(1));
        Assert.True(c.Device<RamModule>("mem").ValueAt(1010) > 1000, "the main loop hardly ran");
        Assert.False(c.IsHalted);
        Assert.Empty(c.MicrocodeWarnings);
    }

    [Fact]
    public void TheTimerMovesTheClockAndKeysAreCounted()
    {
        var (c, lcd) = Demo();
        for (int i = 0; i < 20_000 * 5 + 5_000; i++) c.SingleStep();
        Assert.StartsWith("time 05", lcd.Line(0));
        var keys = c.Device<Keypad>("keys");
        for (int press = 0; press < 3; press++)
        {
            keys.Press(Keypad.Keys.Space);
            for (int i = 0; i < 3_000; i++) c.SingleStep();
            keys.Release(Keypad.Keys.Space);
            for (int i = 0; i < 1_000; i++) c.SingleStep();
        }
        Assert.Contains("keys 03", lcd.Line(0));
    }

    [Fact]
    public void TheStackIsBalancedBackInTheMainLoop()
    {
        var (c, _) = Demo();
        var pic = c.Interrupts;
        int main = c.Assembler.labelLUT["main"], handler = c.Assembler.labelLUT["handler"];
        int checks = 0;
        for (int i = 0; i < 200_000; i++)
        {
            c.SingleStep();
            if (pic.Enabled && c.LastTick?.FetchedFromAddress is int at && at >= main && at < handler)
            {
                Assert.Equal(0, c.Device<Register>("sp").Data);
                checks++;
            }
        }
        Assert.True(checks > 100);
    }
}
