using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class ConsistencyTests
{
    private static MachineDefinition Minimal() => MachineTemplates.Minimal("Mine").Machine;
    private static IReadOnlyList<string> Problems(MachineDefinition machine) => Machine.ValidateDefinition(machine, DeviceRegistry.CreateDefault());

    [Fact]
    public void AMisspeltPortOrConnectionIsReported()
    {
        var machine = Minimal();
        machine.Devices.First(d => d.Id == "pc").Buses["dat"] = "main";
        Assert.Contains(Problems(machine), p => p.Contains("a register has no bus port 'dat', it has data"));

        machine = Minimal();
        machine.Devices.First(d => d.Id == "clk").Bus = "main";
        Assert.Contains(Problems(machine), p => p.Contains("a clock has no bus port 'data', it is on no bus"));

        machine = Minimal();
        machine.Devices.Add(new DeviceDefinition { Id = "a", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "alu", Type = "alu", Bus = "main", Connections = { ["a"] = "a", ["b"] = "a", ["status"] = "status", ["c"] = "a" } });
        Assert.Contains(Problems(machine), p => p.Contains("an alu has no connection 'c'") || p.Contains("a alu has no connection 'c'"));
    }

    [Fact]
    public void ABusMastersDevicesHaveToBeOnItsBus()
    {
        var machine = Minimal();
        machine.Buses.Add(new BusDefinition { Id = "video" });
        machine.Buses.Add(new BusDefinition { Id = "list" });
        machine.Devices.Add(new DeviceDefinition { Id = "fb", Type = "framebuffer", Bus = "video" });
        machine.Devices.Add(new DeviceDefinition { Id = "lmem", Type = "ram", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition
        {
            Id = "rast", Type = "rasterizer", Buses = { ["host"] = "main", ["list"] = "list", ["video"] = "video" },
            Connections = { ["screen"] = "fb", ["memory"] = "lmem" },
        });
        var problem = Assert.Single(Problems(machine));
        Assert.Contains("connection 'memory' is 'lmem', which is on bus 'main', but it has to be on the list bus, 'list'", problem);
        machine.FindDevice("lmem").Bus = "list";
        Assert.Empty(Problems(machine));
    }

    [Fact]
    public void AnIdCanNotContainADot()
    {
        var machine = Minimal();
        machine.Devices.Add(new DeviceDefinition { Id = "a.b", Type = "register", Bus = "main" });
        Assert.Contains(Problems(machine), p => p.Contains("an id can not contain '.'"));
        Assert.Throws<System.ArgumentException>(() => Minimal().RenameDevice("pc", "p.c"));
    }

    [Fact]
    public void TheAluAndItsStatusRegisterCanNotBothWriteInOneStep()
    {
        var machine = Minimal();
        machine.Devices.Add(new DeviceDefinition { Id = "a", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "alu", Type = "alu", Bus = "main", Connections = { ["a"] = "a", ["b"] = "a", ["status"] = "status" } });
        machine.Decoder.Microcode.Instructions.Add(new InstructionDefinition { Mnemonic = "BAD", Operands = 0, Steps = { new MicroStep { Signals = { "alu.cmp", "status.reset", "ir.reset" } } } });
        machine.Decoder.Microcode.Instructions.Add(new InstructionDefinition { Mnemonic = "FINE", Operands = 0, Steps = { new MicroStep { Signals = { "alu.cmp" } }, new MicroStep { Signals = { "status.reset", "ir.reset" } } } });
        var errors = MicrocodeValidator.Validate(machine.Decoder.Microcode, machine).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var error = Assert.Single(errors);
        Assert.Equal("BAD", error.Instruction);
        Assert.Contains("alu.cmp writes the flags into status, and status.reset writes status too", error.Message);
    }

    [Fact]
    public void OnlyTheHaltClockEndsAnInstruction()
    {
        var machine = Minimal();
        machine.Devices.Add(new DeviceDefinition { Id = "other", Type = "clock" });
        machine.Decoder.Microcode.Instructions.Add(new InstructionDefinition { Mnemonic = "STOP", Operands = 0, Steps = { new MicroStep { Signals = { "other.disable" } } } });
        var warnings = MicrocodeValidator.Validate(machine.Decoder.Microcode, machine);
        Assert.Contains(warnings, w => w.Instruction == "STOP");
        Assert.DoesNotContain(warnings, w => w.Instruction == "HLT");
    }

    [Fact]
    public void ABusyBlitterIgnoresStartAndRaisesItsInterruptInTheLatch()
    {
        var host = new Bus("host");
        var video = new Bus("video");
        var cpu = new Register("CPU", "cpu", host);
        var screen = new Framebuffer("FB", "fb", video);
        var blit = new Blitter("BLIT", "blit", host, video, screen);
        var devices = new IBusDevice[] { cpu, blit, screen };
        void Tick() => Clocking.Tick(new[] { host, video }, devices);
        void Give(string line, int value) { cpu.Data = value; cpu.Enable("output"); blit.Enable(line); Tick(); }
        Give("loadw", 4);
        Give("loadh", 1);
        blit.Enable("start"); Tick();
        Assert.True(blit.Busy);
        Tick();
        blit.Enable("start"); Tick();
        int ticks = 0;
        while (blit.Busy) { Tick(); ticks++; }
        Assert.Equal(1, blit.JobsDone);
        Assert.True(blit.TakeInterruptRequest());
        Assert.False(blit.TakeInterruptRequest());
    }

    private sealed class Listener : IBusDevice
    {
        private readonly IInterruptSource source;
        public int Tick, SeenAt = -1;
        public Listener(IInterruptSource source) { this.source = source; }
        public void Drive() { if (source.TakeInterruptRequest() && SeenAt < 0) SeenAt = Tick; }
        public void Latch() { Tick++; }
        public string DisplayName() => "listener";
        public void Enable(string function) { }
        public string ID() => "listener";
        public bool IsOutputEnabled() => false;
        public List<string> SignalLines() => new List<string>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBlittersInterruptIsSeenInTheSameTickWhateverTheDeviceOrder(bool listenerFirst)
    {
        var host = new Bus("host");
        var video = new Bus("video");
        var cpu = new Register("CPU", "cpu", host);
        var screen = new Framebuffer("FB", "fb", video);
        var blit = new Blitter("BLIT", "blit", host, video, screen);
        var listener = new Listener(blit);
        var devices = listenerFirst ? new IBusDevice[] { listener, cpu, blit, screen } : new IBusDevice[] { cpu, blit, screen, listener };
        void Tick() => Clocking.Tick(new[] { host, video }, devices);
        void Give(string line, int value) { cpu.Data = value; cpu.Enable("output"); blit.Enable(line); Tick(); }
        Give("loadw", 2);
        Give("loadh", 1);
        blit.Enable("start"); Tick();
        while (listener.SeenAt < 0) Tick();
        Assert.Equal(7, listener.SeenAt);
    }

    [Fact]
    public void HoverDescribesDirectivesInAnyCase()
    {
        var language = new AssemblyLanguage(BuiltInPackages.Get("BYOC-16").Machine.Decoder.Microcode);
        Assert.Contains("**.DATA**", language.Hover("x: .data 1", 1, 5));
        Assert.Contains("0 cell", language.Hover(".string \"a\"", 1, 3));
        Assert.Contains("old name", language.Hover(".word 1", 1, 3));
    }
}
