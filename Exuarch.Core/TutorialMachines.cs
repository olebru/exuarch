using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // The machine the tutorials build, as it stands after each one, and the built in packages that start each
    // tutorial from there: for whoever skipped one, or lost their machine. TutorialTests runs the same builders
    // against the tutorial pages, so a start can not drift from the page before it.
    public static class TutorialMachines
    {
        public const string EmptyProgram = "; A new machine: write your program here.";
        public const string HaltProgram = "        HLT";
        public const string NopProgram = "        NOP\n        NOP\n        HLT";
        public const string LoopProgram = "loop:   NOP\n        JMP loop";
        public const string HiProgram = "        OUT 'H'\n        OUT 'i'\n        HLT";
        public const string AbcProgram = "        LAI 'A'\n        OUTA\n        LBI 1\n        ADD\n        OUTA\n        ADD\n        OUTA\n        HLT";
        public const string CountdownProgram = "        LAI '5'\nloop:   OUTA\n        LBI 1\n        SUB\n        LBI '0'\n        CMP\n        JNZ loop\n        HLT";
        public const string KeysProgram = "wait:   KEYS\n        LBI 0\n        CMP\n        JNZ got\n        JMP wait\n\ngot:    LBI 16\n        CMP\n        JNZ show\n        HLT\n\nshow:   LBI '0'\n        ADD\n        OUTA\n        JMP wait";
        public const string InterruptProgram = "        SETV handler\n        EI\nmain:   NOP\n        JMP main\n\nhandler: KEYS\n        LBI '0'\n        ADD\n        OUTA\n        ACK\n        RTI";
        public const string TwiceProgram = "        LAI 'O'\n        CALL twice\n        LAI 'K'\n        CALL twice\n        HLT\n\ntwice:  OUTA\n        OUTA\n        RET";

        private static MicroStep Step(params string[] signals) => new MicroStep { Signals = signals.ToList() };
        private static MicroStep When(bool z, params string[] signals) => new MicroStep { When = new FlagCondition { Z = z }, Signals = signals.ToList() };
        private static MicroStep WhenI(bool i, params string[] signals) => new MicroStep { When = new FlagCondition { I = i }, Signals = signals.ToList() };

        private static void Add(MachineDefinition machine, string mnemonic, int operands, OperandType? type, params MicroStep[] steps)
        {
            var instruction = new InstructionDefinition { Mnemonic = mnemonic, Operands = operands, Steps = steps.ToList() };
            if (type.HasValue) instruction.OperandTypes = Enumerable.Repeat(type.Value, operands).ToList();
            machine.Decoder.Microcode.Instructions.Add(instruction);
        }

        // ---- The machine after each tutorial ----

        // Ground zero: the minimal CPU with its microcode cleared to one empty fetch step and no instructions.
        public static MachineDefinition GroundZero()
        {
            var machine = MachineTemplates.Minimal("Mine").Machine;
            var microcode = machine.Decoder.Microcode;
            microcode.Fetch.Steps = new List<MicroStep> { Step() };
            microcode.Instructions.Clear();
            return machine;
        }

        // Fetch: the two steps that read the next opcode into ir.
        public static MachineDefinition Fetch()
        {
            var machine = GroundZero();
            machine.Decoder.Microcode.Fetch.Steps = new List<MicroStep>
            {
                Step("pc.output", "mem.loadmar"),
                Step("mem.output", "ir.load", "pc.inc"),
            };
            return machine;
        }

        // Opcodes are addresses: HLT, opcode 2.
        public static MachineDefinition Halt()
        {
            var machine = Fetch();
            Add(machine, "HLT", 0, null, Step("clk.disable"));
            return machine;
        }

        // Ending an instruction: NOP and JMP.
        public static MachineDefinition Ending()
        {
            var machine = Halt();
            Add(machine, "NOP", 0, null, Step("ir.reset"));
            Add(machine, "JMP", 1, OperandType.Address, Step("pc.output", "mem.loadmar"), Step("mem.output", "pc.load", "ir.reset"));
            return machine;
        }

        // Your first machine: a display and OUT.
        public static MachineDefinition FirstMachine()
        {
            var machine = Ending();
            machine.Devices.Add(new DeviceDefinition { Id = "lcd", Type = "display", Bus = "main" });
            Add(machine, "OUT", 1, OperandType.Value,
                Step("pc.output", "mem.loadmar"),
                Step("mem.output", "lcd.load", "pc.inc", "ir.reset"));
            return machine;
        }

        // Registers and the ALU: a, b, an ALU, and LAI, LBI, ADD and OUTA.
        public static MachineDefinition RegistersAndAlu()
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

        // Loops and flags: SUB, CMP and JNZ.
        public static MachineDefinition LoopsAndFlags()
        {
            var machine = RegistersAndAlu();
            Add(machine, "SUB", 0, null, Step("alu.sub", "a.load", "ir.reset"));
            Add(machine, "CMP", 0, null, Step("alu.cmp", "ir.reset"));
            Add(machine, "JNZ", 1, OperandType.Address,
                Step("pc.output", "mem.loadmar"),
                When(false, "mem.output", "pc.load", "ir.reset"),
                When(true, "pc.inc", "ir.reset"));
            return machine;
        }

        // Subroutines and the stack: a stack pointer, CALL and RET.
        public static MachineDefinition Subroutines()
        {
            var machine = LoopsAndFlags();
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

        // Reading the keypad: a keypad and KEYS.
        public static MachineDefinition ReadingTheKeypad()
        {
            var machine = Subroutines();
            machine.Devices.Add(new DeviceDefinition { Id = "keypad", Type = "keypad", Bus = "main" });
            Add(machine, "KEYS", 0, null, Step("keypad.output", "a.load", "ir.reset"));
            return machine;
        }

        public static MachineDefinition TakingAnInterrupt()
        {
            var machine = ReadingTheKeypad();
            machine.Devices.Add(new DeviceDefinition { Id = "pic", Type = "interruptController", Bus = "main", Connections = { ["irq0"] = "keypad" } });
            machine.Devices.Add(new DeviceDefinition { Id = "vec", Type = "register", Bus = "main" });
            machine.Decoder.Interrupts = "pic";
            machine.Decoder.Microcode.Fetch.Steps = new List<MicroStep>
            {
                WhenI(false, "pc.output", "mem.loadmar"),
                WhenI(false, "mem.output", "ir.load", "pc.inc"),
                WhenI(true, "sp.dec"),
                WhenI(true, "sp.output", "mem.loadmar"),
                WhenI(true, "pc.output", "mem.load", "sp.dec"),
                WhenI(true, "sp.output", "mem.loadmar"),
                WhenI(true, "status.output", "mem.load"),
                WhenI(true, "vec.output", "pc.load", "pic.disable", "ir.reset"),
            };
            Add(machine, "SETV", 1, OperandType.Address, Step("pc.output", "mem.loadmar"), Step("mem.output", "vec.load", "pc.inc", "ir.reset"));
            Add(machine, "EI", 0, null, Step("pic.enable", "ir.reset"));
            Add(machine, "ACK", 0, null, Step("pic.output", "pic.ack", "ir.reset"));
            Add(machine, "RTI", 0, null,
                Step("sp.output", "mem.loadmar"),
                Step("mem.output", "status.load", "sp.inc"),
                Step("sp.output", "mem.loadmar"),
                Step("mem.output", "pc.load", "sp.inc", "pic.enable", "ir.reset"));
            return machine;
        }

        // ---- Where each tutorial starts ----

        // A tutorial that builds on the one before, the machine and program that one leaves, and what the program
        // is called. The first tutorial starts from New… and the last from scratch, so neither is here.
        public sealed record Start(int Number, string GuideId, string Title, string PreviousGuideId, string PreviousTitle, Func<MachineDefinition> Machine, string ProgramName, string Program)
        {
            public string PackageName => $"Tutorial {Number} · {Title}";
        }

        public static readonly IReadOnlyList<Start> Starts = new[]
        {
            new Start(2, "fetch-routine", "Fetch", "ground-zero", "Ground zero", GroundZero, "Empty", EmptyProgram),
            new Start(3, "opcodes-are-addresses", "Opcodes are addresses", "fetch-routine", "Fetch", Fetch, "Empty", EmptyProgram),
            new Start(4, "ending-an-instruction", "Ending an instruction", "opcodes-are-addresses", "Opcodes are addresses", Halt, "Halt", HaltProgram),
            new Start(5, "first-machine", "Your first machine", "ending-an-instruction", "Ending an instruction", Ending, "Loop for ever", LoopProgram),
            new Start(6, "registers-and-the-alu", "Registers and the ALU", "first-machine", "Your first machine", FirstMachine, "Say Hi", HiProgram),
            new Start(7, "loops-and-flags", "Loops and flags", "registers-and-the-alu", "Registers and the ALU", RegistersAndAlu, "A, B, C", AbcProgram),
            new Start(8, "subroutines-and-the-stack", "Subroutines and the stack", "loops-and-flags", "Loops and flags", LoopsAndFlags, "Count down", CountdownProgram),
            new Start(9, "reading-the-keypad", "Reading the keypad", "subroutines-and-the-stack", "Subroutines and the stack", Subroutines, "OK, twice", TwiceProgram),
            new Start(10, "taking-an-interrupt", "Taking an interrupt", "reading-the-keypad", "Reading the keypad", ReadingTheKeypad, "Echo", KeysProgram),
        };

        // The built in package for a start: the machine with every device placed, its program, and a short note.
        public static MachinePackage Package(Start start)
        {
            var machine = start.Machine();
            machine.Name = start.PackageName;
            machine.EnsureLayout(force: true);
            return new MachinePackage
            {
                Name = start.PackageName,
                Description = $"Where the {start.Title} tutorial starts: the machine as {start.PreviousTitle} leaves it.",
                Readme = $"# {start.PackageName}\n\n" +
                         $"This is the machine the [{start.Title}](exuarch:guide/{start.GuideId}) tutorial starts from, built step by step " +
                         $"as the tutorials before it say, ending with [{start.PreviousTitle}](exuarch:guide/{start.PreviousGuideId}). " +
                         "Load it if you skipped a tutorial or lost your own machine, and carry on from there.\n\n" +
                         "It is an example like the others: change it as much as you like, and **Reset** brings it back the way it starts. " +
                         "To keep your work separately, make a copy with **New…**, *Copy of the current machine*.",
                Machine = machine,
                // Laid out as the program editor tidies it, as the shipped examples are.
                Programs = { new PackageProgram { Name = start.ProgramName, Description = $"The program {start.PreviousTitle} ends with", Source = new AssemblyLanguage(machine.Decoder.Microcode, MemoryModule.DefaultSize).FormatDocument(start.Program) } },
            };
        }
    }
}
