using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
using static Exuarch.Core.TutorialMachines;

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

    // ---- The machine after each tutorial: TutorialMachines, which the built in tutorial starts use too ----

    // ---- The programs, as the pages write them ----

    private const string MiddleProgram = "        .DATA 1";

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
        Assert.Equal(19, GroundZero().Devices.Sum(d => registry.Info(d.Type).ControlLines.Count));
        foreach (var text in new[] { "19 control lines", "`pc.output`", "`pc.load`", "`ir.reset`", "`ir.load`", "`ir 0000→0001`", "`FETCH.1`" }) Assert.Contains(text, page);
    }

    // ---- The built in tutorial starts ----

    [Fact]
    public void EveryTutorialThatBuildsOnTheLastHasAStart()
    {
        // Tutorials 2 to 10 build on the one before; 1 starts from New… and 11 from scratch.
        Assert.Equal(Enumerable.Range(2, 9), TutorialMachines.Starts.Select(s => s.Number));
        foreach (var start in TutorialMachines.Starts)
        {
            var package = BuiltInPackages.Get(start.PackageName);
            Assert.Equal(BuiltInPackages.TutorialLevel, BuiltInPackages.Level(start.PackageName));
            Assert.Equal(start.PackageName, package.Machine.Name);
            Assert.Empty(Machine.ValidateDefinition(package.Machine, DeviceRegistry.CreateDefault()));
            // Ground zero's empty fetch step never resets or loads ir, on purpose; nothing else may warn.
            var diagnostics = MicrocodeValidator.Validate(package.Machine.Decoder.Microcode, package.Machine);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            if (start.Number > 2) Assert.Empty(diagnostics);
            // Every device placed, and the program as the page before leaves it, tidy and without problems.
            Assert.All(package.Machine.Devices, d => Assert.NotNull(d.Layout));
            var program = Assert.Single(package.Programs);
            var language = new AssemblyLanguage(package.Machine.Decoder.Microcode, MemoryModule.DefaultSize);
            Assert.Empty(language.Analyze(program.Source).Diagnostics);
            Assert.Equal(program.Source, language.FormatDocument(program.Source));
            // The README leads to this tutorial and the one before, and the tutorial's page leads here.
            Assert.StartsWith($"# {start.PackageName}", package.Readme);
            Assert.All(ReadmeLinks.In(package.Readme), href => Assert.Null(ReadmeLinks.Problem(package, href)));
            Assert.Contains($"exuarch:guide/{start.GuideId})", package.Readme);
            Assert.Contains($"exuarch:guide/{start.PreviousGuideId})", package.Readme);
            Assert.Contains($"(<exuarch:package/{start.PackageName}>)", Guides.Find(start.GuideId).Markdown);
            Assert.Null(ReadmeLinks.Problem(null, $"exuarch:package/{start.PackageName}"));
        }
        // Each start is the previous tutorial's machine: the same builder the tests above check that tutorial with.
        Assert.Equal(Ending().ToJson(), Start(5).Machine().ToJson());
        Assert.Equal("Say Hi", Start(6).ProgramName);
        Assert.Equal("Hi", Lcd(RunHi(BuiltInPackages.Get(Start(6).PackageName))));
    }

    private static TutorialMachines.Start Start(int number) => TutorialMachines.Starts.Single(s => s.Number == number);

    private static Machine RunHi(MachinePackage package)
    {
        var c = new Machine(package.Machine, package.Programs[0].Source);
        RunToHalt(c);
        return c;
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
        var walking = Build(Fetch(), "");
        for (int i = 0; i < 6; i++) walking.SingleStep();
        Assert.Equal(3, walking.Device<Register>("pc").Data);
        Assert.Equal(0, walking.MicroStepRegister);
        Assert.Equal(new int?[] { null, 0, null, 1, null, 2 }, walking.History.Select(t => t.FetchedFromAddress));
        Assert.Equal(new[] { ("main", "mem", 0) }, walking.History[1].Transfers.Select(t => (t.Bus, t.Driver, t.Value)));
        Assert.Equal(new[] { "ir" }, walking.History[1].Transfers[0].Readers);

        // Fetch in one step puts two values on the one bus.
        var oneStep = Fetch();
        oneStep.Decoder.Microcode.Fetch.Steps = new List<MicroStep> { Step("pc.output", "mem.loadmar", "mem.output", "ir.load", "pc.inc") };
        Assert.Contains(MicrocodeValidator.Validate(oneStep.Decoder.Microcode, oneStep),
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("pc.output and mem.output all drive bus 'main'"));

        // Without pc.inc every fetch reads cell 0 again.
        var stuck = Fetch();
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
        var machine = Ending();
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
        var withHalt = Fetch();
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
        var machine = Ending();
        var rom = new DecoderRom(machine.Decoder.Microcode);
        Assert.Equal((3, 4), (rom.FetchByteCodeFromMnemonic("NOP"), rom.FetchByteCodeFromMnemonic("JMP")));
        Assert.Equal(6, rom.OpCodesUsed);

        var nops = Build(machine, NopProgram);
        Assert.Equal(new[] { 3, 3, 2 }, nops.ProgramByteCode);
        Assert.Equal(9, RunToHalt(nops));
        Assert.Equal(new[] { "FETCH.1", "FETCH.2", "NOP.1", "FETCH.1", "FETCH.2", "NOP.1", "FETCH.1", "FETCH.2", "HLT.1" },
            nops.History.Select(t => $"{t.Instruction}.{t.StepIndex + 1}"));

        // Without ir.load the counter runs on from fetch into whatever comes next in the ROM: HLT, whatever memory says.
        var forgot = Ending();
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
        var fallsThrough = Ending();
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
        // Each OUT is its opcode, 6, followed by its operand: 'H' is 72 and 'i' is 105.
        Assert.Equal(new[] { 6, 72, 6, 105, 2 }, c.ProgramByteCode);
        Assert.Equal(11, RunToHalt(c));
        Assert.Equal("Hi", Lcd(c));

        // Without pc.inc the next fetch reads the operand, 'H', as an opcode: ROM address 72 is empty, so the counter
        // walks on through empty words and the machine never halts.
        var forgot = FirstMachine();
        forgot.Decoder.Microcode.FindInstruction("OUT").Steps[1].Signals.Remove("pc.inc");
        var lost = Build(forgot, HiProgram);
        for (int i = 0; i < 40; i++) lost.SingleStep();
        Assert.Equal("H", Lcd(lost));
        Assert.False(lost.IsHalted);
        Assert.Equal(72, lost.History[6].RomAddress);
        Assert.True(lost.MicroStepRegister > 72);

        var page = Page("05-first-machine.md");
        foreach (var text in new[] { "`0006 0048 0006 0069 0002`", "## Forget pc.inc", "ROM address 72", "or click it" }) Assert.Contains(text, page);
        Assert.StartsWith("# Your first machine", page);
        PageShows(page, HiProgram, "pc.output", "mem.loadmar", "mem.output", "lcd.load", "pc.inc", "ir.reset");
        Assert.Contains("`lcd`", page);
        Assert.Contains("11 ticks", page);
    }

    [Fact]
    public void RegistersAndTheAluCountAlongTheAlphabet()
    {
        var c = Build(RegistersAndAlu(), AbcProgram);
        RunToHalt(c);
        Assert.Equal("ABC", Lcd(c));

        // Without a.load the ALU still drives the sum onto the bus, but nothing stores it: a stays 'A'.
        var forgot = RegistersAndAlu();
        forgot.Decoder.Microcode.FindInstruction("ADD").Steps[0].Signals.Remove("a.load");
        var same = Build(forgot, AbcProgram);
        RunToHalt(same);
        Assert.Equal("AAA", Lcd(same));
        Assert.Contains(same.History, t => t.Instruction == "ADD" && t.Transfers.Any(x => x.Driver == "alu" && x.Value == 66 && x.Readers.Count == 0));

        var page = Page("06-registers-and-the-alu.md");
        foreach (var text in new[] { "## Forget a.load", "`AAA`", "for one tick" }) Assert.Contains(text, page);
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
        var c = Build(LoopsAndFlags(), CountdownProgram);
        RunToHalt(c);
        Assert.Equal("54321", Lcd(c));
        // The trace numbers steps as written: the jump is step 2, falling through is step 3.
        Assert.Contains(c.History, t => t.Instruction == "JNZ" && t.StepIndex == 1);
        Assert.Contains(c.History, t => t.Instruction == "JNZ" && t.StepIndex == 2);

        // Without its Z=0 condition, step 2 runs whatever the flags say: JNZ always jumps, and the countdown runs on
        // past '1' through '0' and the characters below it, for ever.
        var always = LoopsAndFlags();
        always.Decoder.Microcode.FindInstruction("JNZ").Steps[1].When = null;
        var runaway = Build(always, CountdownProgram);
        for (int i = 0; i < 230; i++) runaway.SingleStep();
        Assert.False(runaway.IsHalted);
        Assert.StartsWith("543210/.-", Lcd(runaway));

        var page = Page("07-loops-and-flags.md");
        foreach (var text in new[] { "## Forget the condition", "`543210/.-`", "never halts" }) Assert.Contains(text, page);
        Assert.StartsWith("# Loops and flags", page);
        PageShows(page, CountdownProgram, "alu.sub", "alu.cmp", "mem.output", "pc.load", "pc.inc");
        Assert.Contains("Z=0", page);
        Assert.Contains("Z=1", page);
    }

    [Fact]
    public void SubroutinesReturnWhereTheyWereCalled()
    {
        var c = Build(Subroutines(), TwiceProgram);
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
        var c = Build(ReadingTheKeypad(), KeysProgram);
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

        var eager = Build(ReadingTheKeypad(), KeysProgram.Replace("JNZ got", "JMP got"));
        var eagerKeypad = eager.Device<Keypad>("keypad");
        for (int i = 0; i < 100; i++) eager.SingleStep();
        eagerKeypad.Press(Keypad.Keys.Up); eagerKeypad.Release(Keypad.Keys.Up);
        for (int i = 0; i < 2000; i++) eager.SingleStep();
        Assert.False(eager.IsHalted);
        Assert.StartsWith("00", Lcd(eager));
        Assert.Contains("1", Lcd(eager));
        Assert.Equal(16, Lcd(eager).Length);
        eagerKeypad.Press(Keypad.Keys.Space); eagerKeypad.Release(Keypad.Keys.Space);
        RunToHalt(eager, 300);

        var page = Page("09-reading-the-keypad.md");
        Assert.Contains("exuarch:guide/taking-an-interrupt", page);
        Assert.StartsWith("# Reading the keypad", page);
        foreach (var text in new[] { "## Forget to wait", "`JMP got`", "fills with `0`" }) Assert.Contains(text, page);
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

    [Fact]
    public void AnInterruptPrintsTheKeyWhileTheMainLoopNeverAsks()
    {
        var c = Build(TakingAnInterrupt(), InterruptProgram);
        var keypad = c.Device<Keypad>("keypad");
        var sp = c.Device<Register>("sp");
        void Tick(int n) { for (int i = 0; i < n; i++) { c.SingleStep(); Assert.False(c.IsHalted); } }
        void Tap(Keypad.Keys key) { keypad.Press(key); keypad.Release(key); }

        Tick(200);
        Assert.Equal("", Lcd(c));
        Assert.DoesNotContain(c.History, t => t.Instruction == "KEYS");
        Assert.True(c.Interrupts.Enabled);

        Tap(Keypad.Keys.Up);
        Tick(100);
        Assert.Equal("1", Lcd(c));
        Assert.Contains(c.History, t => t.Instruction == "FETCH" && t.StepIndex == 7);
        Assert.Contains(c.History.SelectMany(t => t.Changes), ch => ch.Device == "sp" && ch.After == 65534);
        Assert.Equal(0, sp.Data);
        Assert.Equal(0, c.Interrupts.Pending);
        Assert.Contains(c.History, t => t.Instruction == "RTI");

        keypad.Press(Keypad.Keys.Right);
        Tick(300);
        keypad.Release(Keypad.Keys.Right);
        Assert.Equal("18", Lcd(c));
        Tap(Keypad.Keys.Space);
        Tick(100);
        Assert.Equal("18@", Lcd(c));

        var forgetful = Build(TakingAnInterrupt(), InterruptProgram.Replace("        ACK\n", ""));
        var forgetfulKeypad = forgetful.Device<Keypad>("keypad");
        for (int i = 0; i < 50; i++) forgetful.SingleStep();
        forgetfulKeypad.Press(Keypad.Keys.Up); forgetfulKeypad.Release(Keypad.Keys.Up);
        for (int i = 0; i < 600; i++) forgetful.SingleStep();
        Assert.StartsWith("1000", Lcd(forgetful));
        Assert.Equal(1, forgetful.Interrupts.Pending);

        var page = Page("10-taking-an-interrupt.md");
        Assert.StartsWith("# Taking an interrupt", page);
        PageShows(page, InterruptProgram, "sp.dec", "status.output", "vec.output", "pic.disable", "vec.load", "pic.enable", "pic.ack", "status.load");
        foreach (var text in new[] { "I=1", "I=0", "`pic`", "`vec`", "65534", "`@`", "Tutorial 10 · Taking an interrupt" }) Assert.Contains(text, page);
    }
}
