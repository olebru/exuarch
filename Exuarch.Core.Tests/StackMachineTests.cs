using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

// STACK-16, and the tutorial that builds a stack machine on the minimal CPU. Every number their pages state is
// checked here.
public class StackMachineTests
{
    private static readonly MachinePackage Stack16 = BuiltInPackages.Get("STACK-16");

    private static Machine Run(MachineDefinition machine, string source, out long ticks, out int lowestRp)
    {
        var c = new Machine(machine, source) { RecordHistory = false };
        ticks = 0;
        lowestRp = 0;
        var rp = c.Device<Register>("rp");
        while (!c.IsHalted)
        {
            c.SingleStep();
            Assert.True(++ticks < 5_000_000, "the program did not halt");
            if (rp != null && rp.Data != 0) lowestRp = lowestRp == 0 ? rp.Data : System.Math.Min(lowestRp, rp.Data);
        }
        return c;
    }

    private static string Lcd(Machine c) => c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd();

    // Ticks from the start of an instruction's fetch to the start of the next one.
    private static int Ticks(MachineDefinition machine, string instruction)
    {
        var c = new Machine(machine, instruction + "\n        HLT") { RecordHistory = false };
        int ticks = 0;
        do { c.SingleStep(); ticks++; } while (c.MicroStepRegister != 0);
        return ticks;
    }

    [Fact]
    public void TheProgramsPrintWhatTheReadmeSays()
    {
        var machine = Stack16.Machine;
        Assert.Empty(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine));

        var expression = Run(machine, Stack16.Program("Work out an expression").Source, out var ticks, out _);
        Assert.Equal("8", Lcd(expression));
        Assert.Equal(65, ticks);

        var countdown = Run(machine, Stack16.Program("Count down from 9").Source, out _, out _);
        Assert.Equal("987654321", Lcd(countdown));

        var sum = Run(machine, Stack16.Program("The sum of 1 to 100").Source, out _, out var lowestRp);
        Assert.Equal("5050", Lcd(sum));
        // Four calls deep: the main program's call, and one for each of the three digits in front.
        Assert.Equal(65536 - 4, lowestRp);

        // Every program leaves both stacks empty.
        foreach (var c in new[] { expression, countdown, sum })
        {
            Assert.Equal(0, c.Device<Register>("sp").Data);
            Assert.Equal(0, c.Device<Register>("rp").Data);
        }

        Assert.Equal(6, Ticks(machine, "        PUSH  1"));
        Assert.Equal(7, Ticks(machine, "        ADD"));
        Assert.Equal(4096, machine.FindDevice("mem").Parameters["size"].GetInt32());
        Assert.Equal(64, machine.FindDevice("rmem").Parameters["size"].GetInt32());

        var readme = Stack16.Readme;
        foreach (var text in new[] { "65 ticks", "prints `8`", "`5050`", "PUSH](exuarch:instruction/PUSH) takes 6 ticks", "ADD](exuarch:instruction/ADD) takes 7", "4096 cell memory", "64 cells", "count down four times" })
        {
            Assert.Contains(text, readme);
        }
    }

    [Fact]
    public void TheStackGrowsDownFromTheLastCellAndEveryInstructionKeepsItTidy()
    {
        var c = Run(Stack16.Machine, @"
        PUSH  10
        PUSH  20
        PUSH  30
        SWAP
        OVER
        DUP
        SUB
        POP
        PUSHA value
        LOAD
        ADD
        PUSH  99
        PUSHA value
        STORE
        HLT
value:  .DATA 5", out _, out _);
        var mem = c.Device<RamModule>("mem");
        // 10 20 30 -> SWAP 10 30 20 -> OVER 10 30 20 30 -> DUP 10 30 20 30 30 -> SUB 10 30 20 0 -> POP 10 30 20
        // -> PUSHA value LOAD 10 30 20 5 -> ADD 10 30 25 -> 99 value STORE leaves 10 30 25, with 99 at value.
        Assert.Equal(new[] { 10, 30, 25 }, new[] { 4095, 4094, 4093 }.Select(mem.ValueAt));
        Assert.Equal(65536 - 3, c.Device<Register>("sp").Data);
        // The popped 99 is still in the cell below the top: a pop only moves the pointer.
        Assert.Equal(99, mem.ValueAt(4092));
        Assert.Equal(99, mem.ValueAt(c.Assembler.labelLUT["value"]));
    }

    [Theory]
    [InlineData("JZ", 0, true)]
    [InlineData("JZ", 3, false)]
    [InlineData("JN", 65535, true)]
    [InlineData("JN", 3, false)]
    public void ConditionalJumpsPopTheValueTheyTest(string jump, int value, bool taken)
    {
        var c = Run(Stack16.Machine, $@"
        PUSH  {value}
        {jump}    yes
        PUSH  1
        HLT
yes:    PUSH  2
        HLT", out _, out _);
        Assert.Equal(taken ? 2 : 1, c.Device<RamModule>("mem").ValueAt(4095));
        Assert.Equal(65535, c.Device<Register>("sp").Data);
    }

    // ---- The tutorial ----

    private static string Page()
    {
        using var stream = typeof(BuiltInPackages).Assembly.GetManifestResourceStream("Exuarch.Core.Guides/tutorials/11-stack-machine.md");
        Assert.True(stream != null, "the tutorial is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    private static MicroStep Step(params string[] signals) => new MicroStep { Signals = signals.ToList() };

    // The minimal CPU with what the page adds, in the order it adds it.
    private static MachineDefinition TutorialMachine()
    {
        var machine = MachineTemplates.Minimal("Mine").Machine;
        machine.Devices.Add(new DeviceDefinition { Id = "sp", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "x", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "y", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "alu", Type = "alu", Bus = "main", Connections = { ["a"] = "x", ["b"] = "y", ["status"] = "status" } });
        machine.Devices.Add(new DeviceDefinition { Id = "lcd", Type = "display", Bus = "main" });
        var instructions = machine.Decoder.Microcode.Instructions;
        instructions.Add(new InstructionDefinition
        {
            Mnemonic = "PUSH", Operands = 1, OperandTypes = new List<OperandType> { OperandType.Value },
            Steps = { Step("pc.output", "mem.loadmar", "sp.dec"), Step("mem.output", "x.load", "pc.inc"), Step("sp.output", "mem.loadmar"), Step("x.output", "mem.load", "ir.reset") },
        });
        instructions.Add(new InstructionDefinition
        {
            Mnemonic = "ADD", Operands = 0,
            Steps = { Step("sp.output", "mem.loadmar"), Step("mem.output", "y.load", "sp.inc"), Step("sp.output", "mem.loadmar"), Step("mem.output", "x.load"), Step("alu.add", "mem.load", "ir.reset") },
        });
        instructions.Add(new InstructionDefinition
        {
            Mnemonic = "OUT", Operands = 0,
            Steps = { Step("sp.output", "mem.loadmar"), Step("mem.output", "lcd.load", "sp.inc", "ir.reset") },
        });
        return machine;
    }

    private const string Program = "        PUSH 2\n        PUSH 3\n        ADD\n        PUSH 4\n        ADD\n        PUSH '0'\n        ADD\n        OUT\n        HLT";

    [Fact]
    public void TheTutorialsStackMachineAddsUpToNine()
    {
        var machine = TutorialMachine();
        Assert.Empty(Machine.ValidateDefinition(machine, DeviceRegistry.CreateDefault()));
        Assert.Empty(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine));
        var analysis = new AssemblyLanguage(machine.Decoder.Microcode, MemoryModule.DefaultSize).Analyze(Program);
        Assert.True(analysis.Diagnostics.Count == 0, string.Join(" | ", analysis.Diagnostics));

        var c = new Machine(machine, Program);
        var sp = c.Device<Register>("sp");
        var mem = c.Device<RamModule>("mem");
        // The stack after each instruction: sp, and the cells from 4095 down to sp.
        var after = new List<(int Sp, int[] Stack)>();
        int ticks = 0;
        while (!c.IsHalted)
        {
            c.SingleStep();
            ticks++;
            if (c.MicroStepRegister == 0 && !c.IsHalted)
            {
                var top = sp.Data == 0 ? 4096 : sp.Data - 65536 + 4096;
                after.Add((sp.Data, Enumerable.Range(top, 4096 - top).Reverse().Select(mem.ValueAt).ToArray()));
            }
        }
        Assert.Equal("9", c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd());
        Assert.Equal(52, ticks);
        var expected = new (int, int[])[]
        {
            (65535, new[] { 2 }), (65534, new[] { 2, 3 }), (65535, new[] { 5 }), (65534, new[] { 5, 4 }),
            (65535, new[] { 9 }), (65534, new[] { 9, 48 }), (65535, new[] { 57 }), (0, new int[0]),
        };
        Assert.Equal(expected.Length, after.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, after[i].Sp);
            Assert.Equal(expected[i].Item2, after[i].Stack);
        }
        Assert.Equal(48, mem.ValueAt(4094));

        // The first PUSH, tick by tick, after the two ticks of fetch.
        var push = c.History.Skip(2).Take(4).ToList();
        Assert.Equal(new[] { "PUSH.1", "PUSH.2", "PUSH.3", "PUSH.4" }, push.Select(t => $"{t.Instruction}.{t.StepIndex + 1}"));
        Assert.Contains(push[0].Changes, ch => ch.Device == "sp" && ch.Before == 0 && ch.After == 0xFFFF);
        Assert.Contains(push[1].Transfers, t => t.Driver == "mem" && t.Value == 2 && t.Readers.Contains("x"));
        Assert.Contains(push[2].Transfers, t => t.Driver == "sp" && t.Value == 0xFFFF && t.Readers.Contains("mem"));
        Assert.Contains(push[3].Writes, w => w.Device == "mem" && w.Address == 0x0FFF && w.Value == 2);

        Assert.Equal(6, Ticks(machine, "        PUSH 1"));
        Assert.Equal(7, Ticks(machine, "        ADD"));

        var page = Page();
        Assert.StartsWith("# A stack machine", page);
        Assert.Contains("```asm\n" + Program + "\n```", page);
        foreach (var signal in new[] { "pc.output", "mem.loadmar", "sp.dec", "mem.output", "x.load", "pc.inc", "sp.output", "x.output", "mem.load", "ir.reset", "y.load", "sp.inc", "alu.add", "lcd.load", "alu.sub" })
        {
            Assert.Contains($"`{signal}`", page);
        }
        foreach (var text in new[] { "52 ticks", "takes 6 ticks", "takes 7 ticks", "`mem[0FFF]=0002`", "`0000` to `FFFF`", "4096 cells", "cell 4094 still holds the 48", "## Next" })
        {
            Assert.Contains(text, page);
        }
        // The minimal CPU's memory has no size set, so it has the default 4096 cells the page says.
        Assert.False(machine.FindDevice("mem").Parameters.ContainsKey("size"));
        Assert.Equal(4096, MemoryModule.DefaultSize);
    }
}
