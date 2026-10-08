using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

// The handbook tutorials, done by hand: each builds the machine its page describes on top of the one before,
// runs the page's program and checks what it shows. The page has to contain the signals and program lines the
// test uses, so the text can not drift away from a machine that works.
public class TutorialTests
{
    private static string Page(string file)
    {
        using var stream = typeof(BuiltInPackages).Assembly.GetManifestResourceStream($"Exuarch.Core.Guides/tutorials/{file}");
        Assert.True(stream != null, $"tutorial {file} is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static MicroStep Step(params string[] signals) => new MicroStep { Signals = signals.ToList() };
    private static MicroStep When(bool z, params string[] signals) => new MicroStep { When = new FlagCondition { Z = z }, Signals = signals.ToList() };

    private static void Add(MachineDefinition machine, string mnemonic, int operands, OperandType? type, params MicroStep[] steps)
    {
        var instruction = new InstructionDefinition { Mnemonic = mnemonic, Operands = operands, Steps = steps.ToList() };
        if (type.HasValue) instruction.OperandTypes = Enumerable.Repeat(type.Value, operands).ToList();
        machine.Decoder.Microcode.Instructions.Add(instruction);
    }

    // ---- The machine after each tutorial ----

    // The minimal CPU with its microcode cleared to one empty fetch step and no instructions.
    private static MachineDefinition GroundZero()
    {
        var machine = MachineTemplates.Minimal("Mine").Machine;
        var microcode = machine.Decoder.Microcode;
        microcode.Fetch.Steps = new List<MicroStep> { Step() };
        microcode.Instructions.Clear();
        return machine;
    }

    private static MachineDefinition WithFetch()
    {
        var machine = GroundZero();
        machine.Decoder.Microcode.Fetch.Steps = new List<MicroStep>
        {
            Step("pc.output", "mem.loadmar"),
            Step("mem.output", "ir.load", "pc.inc"),
        };
        return machine;
    }

    private static MachineDefinition FetchFromScratch()
    {
        var machine = WithFetch();
        Add(machine, "HLT", 0, null, Step("clk.disable"));
        Add(machine, "NOP", 0, null, Step("ir.reset"));
        Add(machine, "JMP", 1, OperandType.Address, Step("pc.output", "mem.loadmar"), Step("mem.output", "pc.load", "ir.reset"));
        return machine;
    }

    private static MachineDefinition FirstMachine()
    {
        var machine = FetchFromScratch();
        machine.Devices.Add(new DeviceDefinition { Id = "lcd", Type = "display", Bus = "main" });
        Add(machine, "OUT", 1, OperandType.Value,
            Step("pc.output", "mem.loadmar"),
            Step("mem.output", "lcd.load", "pc.inc", "ir.reset"));
        return machine;
    }

    private static MachineDefinition WithAlu()
    {
        var machine = FirstMachine();
        machine.Devices.Add(new DeviceDefinition { Id = "a", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "b", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "alu", Type = "alu", Bus = "main", Connections = { ["a"] = "a", ["b"] = "b", ["status"] = "status" } });
        Add(machine, "LAI", 1, OperandType.Value, Step("pc.output", "mem.loadmar"), Step("mem.output", "a.load", "pc.inc", "ir.reset"));
        Add(machine, "LBI", 1, OperandType.Value, Step("pc.output", "mem.loadmar"), Step("mem.output", "b.load", "pc.inc", "ir.reset"));
        Add(machine, "ADD", 0, null, Step("alu.add", "a.load", "ir.reset"));
        Add(machine, "OUTA", 0, null, Step("a.output", "lcd.load", "ir.reset"));
        return machine;
    }

    private static MachineDefinition WithLoops()
    {
        var machine = WithAlu();
        Add(machine, "SUB", 0, null, Step("alu.sub", "a.load", "ir.reset"));
        Add(machine, "CMP", 0, null, Step("alu.cmp", "ir.reset"));
        Add(machine, "JNZ", 1, OperandType.Address,
            Step("pc.output", "mem.loadmar"),
            When(false, "mem.output", "pc.load", "ir.reset"),
            When(true, "pc.inc", "ir.reset"));
        return machine;
    }

    private static MachineDefinition WithStack()
    {
        var machine = WithLoops();
        machine.Devices.Add(new DeviceDefinition { Id = "tmp", Type = "register", Bus = "main" });
        machine.Devices.Add(new DeviceDefinition { Id = "sp", Type = "register", Bus = "main" });
        Add(machine, "CALL", 1, OperandType.Address,
            Step("pc.output", "mem.loadmar"),
            Step("mem.output", "tmp.load", "pc.inc", "sp.dec"),
            Step("sp.output", "mem.loadmar"),
            Step("pc.output", "mem.load"),
            Step("tmp.output", "pc.load", "ir.reset"));
        Add(machine, "RET", 0, null,
            Step("sp.output", "mem.loadmar"),
            Step("mem.output", "pc.load", "sp.inc", "ir.reset"));
        return machine;
    }

    private static MachineDefinition WithKeypad()
    {
        var machine = WithStack();
        machine.Devices.Add(new DeviceDefinition { Id = "keypad", Type = "keypad", Bus = "main" });
        Add(machine, "KEYS", 0, null, Step("keypad.output", "a.load", "ir.reset"));
        return machine;
    }

    // ---- The programs, as the pages write them ----

    private const string HaltProgram = "        HLT";
    private const string MiddleProgram = "        .DATA 1";
    private const string NopProgram = "        NOP\n        NOP\n        HLT";
    private const string LoopProgram = "loop:   NOP\n        JMP loop";
    private const string HiProgram = "        OUT 'H'\n        OUT 'i'\n        HLT";
    private const string AbcProgram = "        LAI 'A'\n        OUTA\n        LBI 1\n        ADD\n        OUTA\n        ADD\n        OUTA\n        HLT";
    private const string CountdownProgram = "        LAI '5'\nloop:   OUTA\n        LBI 1\n        SUB\n        LBI '0'\n        CMP\n        JNZ loop\n        HLT";
    private const string TwiceProgram = "        LAI 'O'\n        CALL twice\n        LAI 'K'\n        CALL twice\n        HLT\n\ntwice:  OUTA\n        OUTA\n        RET";
    private const string KeysProgram = "wait:   KEYS\n        LBI 0\n        CMP\n        JNZ got\n        JMP wait\n\ngot:    LBI 16\n        CMP\n        JNZ show\n        HLT\n\nshow:   LBI '0'\n        ADD\n        OUTA\n        JMP wait";

    private static Machine Build(MachineDefinition machine, string program)
    {
        Assert.Empty(Machine.ValidateDefinition(machine, DeviceRegistry.CreateDefault()));
        Assert.DoesNotContain(MicrocodeValidator.Validate(machine.Decoder.Microcode, machine), d => d.Severity == DiagnosticSeverity.Error);
        var analysis = new AssemblyLanguage(machine.Decoder.Microcode, MemoryModule.DefaultSize).Analyze(program);
        Assert.True(analysis.Diagnostics.Count == 0, string.Join(" | ", analysis.Diagnostics));
        return new Machine(machine, program);
    }

    private static int RunToHalt(Machine c, int limit = 2000)
    {
        int ticks = 0;
        while (!c.IsHalted) { c.SingleStep(); Assert.True(++ticks < limit, "the program did not halt"); }
        return ticks;
    }

    private static string Lcd(Machine c) => c.Device<CharacterDisplay>("lcd").Line(0).TrimEnd();

    // The page shows every program in an asm block and names each signal of the new instructions.
    private static void PageShows(string page, string program, params string[] signals)
    {
        Assert.Contains("```asm\n" + program + "\n```", page.Replace("\r\n", "\n"));
        foreach (var signal in signals) Assert.Contains($"`{signal}`", page);
    }

    [Fact]
    public void AtGroundZeroTheCounterCounts()
    {
        // One empty step at ROM address 0, so nothing happens but the counter counting.
        var empty = Build(GroundZero(), "");
        for (int i = 0; i < 5; i++) empty.SingleStep();
        Assert.Equal(5, empty.MicroStepRegister);
        Assert.Equal(0, empty.Device<Register>("pc").Data);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, empty.History.Select(t => t.RomAddress));
        Assert.All(empty.History, t => Assert.Empty(t.Signals));
        Assert.Equal("FETCH", empty.History[0].Instruction);
        Assert.All(empty.History.Skip(1), t => Assert.Null(t.Instruction));
        Assert.All(empty.History, t => Assert.Equal(new[] { ("ir", t.MicroStep, t.MicroStep + 1) }, t.Changes.Select(c => (c.Device, c.Before, c.After))));
        Assert.Equal(1, new DecoderRom(GroundZero().Decoder.Microcode).OpCodesUsed);

        var page = Page("01-ground-zero.md");
        Assert.StartsWith("# Ground zero", page);
        var registry = DeviceRegistry.CreateDefault();
        Assert.Equal(17, GroundZero().Devices.Sum(d => registry.Info(d.Type).ControlLines.Count));
        foreach (var text in new[] { "17 control lines", "`pc.output`", "`pc.load`", "`ir.reset`", "`ir.load`", "`ir 0000→0001`", "`FETCH.1`" }) Assert.Contains(text, page);
    }

    [Fact]
    public void OneWordMakesALoop()
    {
        // pc.inc alone is on for the first tick only: after it the counter has moved past address 0.
        var once = GroundZero();
        once.Decoder.Microcode.Fetch.Steps = new List<MicroStep> { Step("pc.inc") };
        var counting = Build(once, "");
        for (int i = 0; i < 5; i++) counting.SingleStep();
        Assert.Equal(1, counting.Device<Register>("pc").Data);
        Assert.Equal(5, counting.MicroStepRegister);

        // With ir.reset in the same word the counter comes back to it every tick.
        var loop = GroundZero();
        loop.Decoder.Microcode.Fetch.Steps = new List<MicroStep> { Step("pc.inc", "ir.reset") };
        var looping = Build(loop, "");
        for (int i = 0; i < 10; i++) looping.SingleStep();
        Assert.Equal(10, looping.Device<Register>("pc").Data);
        Assert.Equal(0, looping.MicroStepRegister);

        // The exercise: two words, so pc counts on every second tick.
        var everySecond = GroundZero();
        everySecond.Decoder.Microcode.Fetch.Steps = new List<MicroStep> { Step("pc.inc"), Step("ir.reset") };
        var slower = Build(everySecond, "");
        for (int i = 0; i < 10; i++) slower.SingleStep();
        Assert.Equal(5, slower.Device<Register>("pc").Data);

        var page = Page("01-ground-zero.md");
        foreach (var text in new[] { "## 7. Switch one line on", "`pc.inc`", "`ir.reset`", "goes to 1 on the first tick", "1, 2, 3 … 10", "step 1 `pc.inc`, step 2 `ir.reset`", "After ten ticks `pc` is 5" })
            Assert.Contains(text, page);
    }

    [Fact]
    public void FetchWalksThroughEmptyMemory()
    {
        // Every cell is 0, the opcode of fetch, so the machine walks through memory.
        var walking = Build(WithFetch(), "");
        for (int i = 0; i < 6; i++) walking.SingleStep();
        Assert.Equal(3, walking.Device<Register>("pc").Data);
        Assert.Equal(0, walking.MicroStepRegister);
        Assert.Equal(new int?[] { null, 0, null, 1, null, 2 }, walking.History.Select(t => t.FetchedFromAddress));
        Assert.Equal(new[] { ("main", "mem", 0) }, walking.History[1].Transfers.Select(t => (t.Bus, t.Driver, t.Value)));
        Assert.Equal(new[] { "ir" }, walking.History[1].Transfers[0].Readers);

        // Fetch in one step puts two values on the one bus.
        var oneStep = WithFetch();
        oneStep.Decoder.Microcode.Fetch.Steps = new List<MicroStep> { Step("pc.output", "mem.loadmar", "mem.output", "ir.load", "pc.inc") };
        Assert.Contains(MicrocodeValidator.Validate(oneStep.Decoder.Microcode, oneStep),
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("pc.output and mem.output all drive bus 'main'"));

        // Without pc.inc every fetch reads cell 0 again.
        var stuck = WithFetch();
        stuck.Decoder.Microcode.Fetch.Steps[1] = Step("mem.output", "ir.load");
        var standing = Build(stuck, "");
        for (int i = 0; i < 6; i++) standing.SingleStep();
        Assert.Equal(0, standing.Device<Register>("pc").Data);
        Assert.Equal(new int?[] { null, 0, null, 0, null, 0 }, standing.History.Select(t => t.FetchedFromAddress));

        var page = Page("02-fetch-routine.md");
        Assert.StartsWith("# Fetch", page);
        foreach (var text in new[] { "the MAR, points at", "## 7. Forget pc.inc", "`pc` stays at 0", "marked **PC**" }) Assert.Contains(text, page);
        foreach (var text in new[] { "`pc.output` `mem.loadmar`", "`mem.output` `ir.load` `pc.inc`", "pc.output and mem.output all drive bus 'main'", "`mem → main 0000 → ir`" }) Assert.Contains(text, page);
    }

    [Fact]
    public void AnOpcodeIsWhereTheStepsStart()
    {
        var machine = FetchFromScratch();
        var rom = new DecoderRom(machine.Decoder.Microcode);
        Assert.Equal(2, rom.FetchByteCodeFromMnemonic("HLT"));
        Assert.Equal(0x10003, DecoderRom.RomAddress(StatusRegister.ZeroFlag, 3));

        var halt = Build(machine, HaltProgram);
        Assert.Equal(new[] { 2 }, halt.ProgramByteCode);
        Assert.Equal(3, RunToHalt(halt));
        Assert.Equal(new[] { 0, 1, 2 }, halt.History.Select(t => t.RomAddress));
        Assert.Equal(("pc", 0, "mem"), (halt.History[0].Transfers[0].Driver, halt.History[0].Transfers[0].Value, halt.History[0].Transfers[0].Readers.Single()));
        Assert.Equal(("mem", 2, "ir"), (halt.History[1].Transfers[0].Driver, halt.History[1].Transfers[0].Value, halt.History[1].Transfers[0].Readers.Single()));

        // Any number is an opcode: 1 is the address of FETCH.2, so the machine jumps into the middle of fetch. The MAR
        // still holds 0, so FETCH.2 reads cell 0 again and again, while pc counts on.
        var withHalt = WithFetch();
        Add(withHalt, "HLT", 0, null, Step("clk.disable"));
        var middle = Build(withHalt, MiddleProgram);
        for (int i = 0; i < 6; i++) middle.SingleStep();
        Assert.Equal(new[] { 0, 1, 1, 1, 1, 1 }, middle.History.Select(t => t.RomAddress));
        Assert.Equal(5, middle.Device<Register>("pc").Data);
        Assert.All(middle.History.Skip(1), t => Assert.Equal(("mem", 1), (t.Transfers[0].Driver, t.Transfers[0].Value)));
        Assert.False(middle.IsHalted);

        var page = Page("03-opcodes-are-addresses.md");
        Assert.StartsWith("# Opcodes are addresses", page);
        PageShows(page, MiddleProgram);
        foreach (var text in new[] { "## 7. Any number is an opcode", "HLT is opcode 5", "never goes back to fetch", "`FETCH.2`, `FETCH.2`" }) Assert.Contains(text, page);
        PageShows(page, HaltProgram, "clk.disable", "ir.reset");
        foreach (var text in new[] { "`HLT` is opcode 2", "3 ticks", "`0002`", "`10003`", "`pc → main 0000 → mem`", "`mem → main 0002 → ir`" }) Assert.Contains(text, page);
    }

    [Fact]
    public void InstructionsEndByGoingBackToFetch()
    {
        var machine = FetchFromScratch();
        var rom = new DecoderRom(machine.Decoder.Microcode);
        Assert.Equal((3, 4), (rom.FetchByteCodeFromMnemonic("NOP"), rom.FetchByteCodeFromMnemonic("JMP")));
        Assert.Equal(6, rom.OpCodesUsed);

        var nops = Build(machine, NopProgram);
        Assert.Equal(new[] { 3, 3, 2 }, nops.ProgramByteCode);
        Assert.Equal(9, RunToHalt(nops));
        Assert.Equal(new[] { "FETCH.1", "FETCH.2", "NOP.1", "FETCH.1", "FETCH.2", "NOP.1", "FETCH.1", "FETCH.2", "HLT.1" },
            nops.History.Select(t => $"{t.Instruction}.{t.StepIndex + 1}"));

        // Without ir.load the counter runs on from fetch into whatever comes next in the ROM: HLT, whatever memory says.
        var forgot = FetchFromScratch();
        forgot.Decoder.Microcode.Fetch.Steps[1].Signals.Remove("ir.load");
        Assert.Contains(MicrocodeValidator.Validate(forgot.Decoder.Microcode, forgot), d => d.Instruction == "FETCH" && d.Message.Contains("never resets or loads 'ir'"));
        var runsOn = new Machine(forgot, NopProgram);
        Assert.Equal(3, RunToHalt(runsOn));
        Assert.Equal("HLT", runsOn.History.Last().Instruction);

        // JMP loops for ever: NOP takes three ticks and JMP four, and then the program counter is back at 0.
        var loop = Build(machine, LoopProgram);
        Assert.Equal(new[] { 3, 4, 0 }, loop.ProgramByteCode);
        for (int i = 0; i < 7; i++) loop.SingleStep();
        Assert.Equal((0, 0), (loop.Device<Register>("pc").Data, loop.MicroStepRegister));
        for (int i = 0; i < 700; i++) loop.SingleStep();
        Assert.False(loop.IsHalted);

        // Without ir.reset, NOP runs on into JMP's steps: JMP reads the cell after NOP as its address, which is JMP's
        // own opcode, 4, and the machine walks off through empty memory for ever.
        var fallsThrough = FetchFromScratch();
        fallsThrough.Decoder.Microcode.FindInstruction("NOP").Steps[0].Signals.Clear();
        Assert.Contains(MicrocodeValidator.Validate(fallsThrough.Decoder.Microcode, fallsThrough), d => d.Instruction == "NOP" && d.Message.Contains("never resets or loads 'ir'"));
        var walking = new Machine(fallsThrough, LoopProgram);
        for (int i = 0; i < 5; i++) walking.SingleStep();
        Assert.Equal(new[] { "FETCH.1", "FETCH.2", "NOP.1", "JMP.1", "JMP.2" }, walking.History.Select(t => $"{t.Instruction}.{t.StepIndex + 1}"));
        Assert.Equal(4, walking.Device<Register>("pc").Data);
        for (int i = 0; i < 200; i++) walking.SingleStep();
        Assert.False(walking.IsHalted);
        Assert.True(walking.Device<Register>("pc").Data > 50);

        var page = Page("04-ending-an-instruction.md");
        Assert.StartsWith("# Ending an instruction", page);
        foreach (var text in new[] { "## 7. Forget ir.reset", "runs straight on into `JMP`", "`JMP`'s own opcode, 4" }) Assert.Contains(text, page);
        PageShows(page, NopProgram, "ir.reset", "ir.load");
        PageShows(page, LoopProgram, "pc.output", "mem.loadmar", "mem.output", "pc.load");
        foreach (var text in new[] { "`NOP` is opcode 3", "`JMP` is opcode 4", "9 ticks", "3 ticks", "`0003 0003 0002`", "`0003 0004 0000`" }) Assert.Contains(text, page);
        // The ROM table lists every step.
        foreach (var block in rom.Blocks)
        {
            for (int i = 0; i < block.Count; i++)
            {
                var signals = string.Join(" ", block.Instruction.Steps[i].Signals.Select(x => $"`{x}`"));
                Assert.Contains($"| `{block.Base + i:X5}` | `{block.Instruction.Mnemonic}.{i + 1}` | {signals} |", page);
            }
        }
    }

    [Fact]
    public void YourFirstMachineSaysHi()
    {
        var c = Build(FirstMachine(), HiProgram);
        Assert.Equal(11, RunToHalt(c));
        Assert.Equal("Hi", Lcd(c));
        var page = Page("05-first-machine.md");
        Assert.StartsWith("# Your first machine", page);
        PageShows(page, HiProgram, "pc.output", "mem.loadmar", "mem.output", "lcd.load", "pc.inc", "ir.reset");
        Assert.Contains("`lcd`", page);
        Assert.Contains("11 ticks", page);
    }

    [Fact]
    public void RegistersAndTheAluCountAlongTheAlphabet()
    {
        var c = Build(WithAlu(), AbcProgram);
        RunToHalt(c);
        Assert.Equal("ABC", Lcd(c));
        var page = Page("06-registers-and-the-alu.md");
        Assert.StartsWith("# Registers and the ALU", page);
        PageShows(page, AbcProgram, "a.load", "b.load", "alu.add", "a.output");
        foreach (var name in new[] { "LAI", "LBI", "ADD", "OUTA" }) Assert.Contains($"`{name}`", page);
        // The page points out the status register the minimal CPU comes with, the one the ALU writes and the decoder reads.
        var minimal = MachineTemplates.Minimal("Mine").Machine;
        Assert.Equal("statusRegister", minimal.FindDevice("status").Type);
        Assert.Equal("status", minimal.Decoder.Status);
        Assert.Contains("## Find the status register", page);
        Assert.Contains("**Status register** is set to `status`", page);
        Assert.Contains("the decoder's **status** socket", page);
    }

    [Fact]
    public void LoopsAndFlagsCountDown()
    {
        var c = Build(WithLoops(), CountdownProgram);
        RunToHalt(c);
        Assert.Equal("54321", Lcd(c));
        // The trace numbers steps as written: the jump is step 2, falling through is step 3.
        Assert.Contains(c.History, t => t.Instruction == "JNZ" && t.StepIndex == 1);
        Assert.Contains(c.History, t => t.Instruction == "JNZ" && t.StepIndex == 2);
        var page = Page("07-loops-and-flags.md");
        Assert.StartsWith("# Loops and flags", page);
        PageShows(page, CountdownProgram, "alu.sub", "alu.cmp", "mem.output", "pc.load", "pc.inc");
        Assert.Contains("Z=0", page);
        Assert.Contains("Z=1", page);
    }

    [Fact]
    public void SubroutinesReturnWhereTheyWereCalled()
    {
        var c = Build(WithStack(), TwiceProgram);
        RunToHalt(c);
        Assert.Equal("OOKK", Lcd(c));
        // Every push was popped again.
        Assert.Equal(0, c.Device<Register>("sp").Data);
        Assert.Equal(65535, c.History.SelectMany(t => t.Changes).First(ch => ch.Device == "sp").After);
        var page = Page("08-subroutines-and-the-stack.md");
        Assert.StartsWith("# Subroutines and the stack", page);
        PageShows(page, TwiceProgram, "tmp.load", "sp.dec", "sp.output", "mem.load", "tmp.output", "sp.inc");
        Assert.DoesNotContain("initialValue", page);
        Assert.Contains("65535", page);
    }

    [Fact]
    public void TheKeypadEchoesKeysUntilSpace()
    {
        var c = Build(WithKeypad(), KeysProgram);
        var keypad = c.Device<Keypad>("keypad");
        void Tick(int n) { for (int i = 0; i < n; i++) { c.SingleStep(); Assert.False(c.IsHalted, "halted before space"); } }
        void Tap(Keypad.Keys key) { keypad.Press(key); keypad.Release(key); }

        // Nothing pressed: the program waits.
        Tick(200);
        Assert.Equal("", Lcd(c));
        // A tap is remembered until the program reads it, and shown once.
        Tap(Keypad.Keys.Up);
        Tick(200);
        Assert.Equal("1", Lcd(c));
        Tap(Keypad.Keys.Right);
        Tick(200);
        Assert.Equal("18", Lcd(c));
        Tap(Keypad.Keys.Space);
        RunToHalt(c, 200);
        Assert.Equal("18", Lcd(c));

        var page = Page("09-reading-the-keypad.md");
        Assert.StartsWith("# Reading the keypad", page);
        PageShows(page, KeysProgram, "keypad.output", "a.load");
        Assert.Contains("`keypad`", page);
    }

    [Fact]
    public void ATutorialsLinksLeadSomewhere()
    {
        foreach (var file in new[] { "01-ground-zero.md", "02-fetch-routine.md", "03-opcodes-are-addresses.md", "04-ending-an-instruction.md", "05-first-machine.md", "06-registers-and-the-alu.md", "07-loops-and-flags.md", "08-subroutines-and-the-stack.md", "09-reading-the-keypad.md" })
        {
            var page = Page(file);
            Assert.Contains("## Next", page);
            Assert.NotEmpty(ReadmeLinks.In(page));
        }
    }
}
