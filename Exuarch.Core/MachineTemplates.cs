using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public static class MachineTemplates
    {
        public const string StarterProgram = "; A new machine: write your program here.\n        NOP\n        HLT";

        public static MachinePackage Minimal(string name)
        {
            MicroStep Step(params string[] signals) => new MicroStep { Signals = signals.ToList() };
            var machine = new MachineDefinition
            {
                Name = name,
                Buses = { new BusDefinition { Id = "main" } },
                Devices =
                {
                    new DeviceDefinition { Id = "pc", Type = "register", Name = "PC", Bus = "main" },
                    new DeviceDefinition { Id = "ir", Type = "instructionRegister", Name = "IR", Bus = "main" },
                    new DeviceDefinition { Id = "mem", Type = "ram", Name = "MEMORY", Bus = "main" },
                    new DeviceDefinition { Id = "status", Type = "statusRegister", Name = "STATUS", Bus = "main" },
                    new DeviceDefinition { Id = "clk", Type = "clock", Name = "CLOCK" },
                },
                Decoder = new DecoderDefinition
                {
                    Status = "status",
                    InstructionRegister = "ir",
                    Microcode = new MicrocodeDefinition
                    {
                        Name = $"{name} microcode",
                        Fetch = new InstructionDefinition
                        {
                            Mnemonic = "FETCH",
                            Description = "Load the opcode at the program counter into the instruction register and step past it",
                            Steps = { Step("pc.output", "mem.loadmar"), Step("mem.output", "ir.load", "pc.inc") },
                        },
                        Instructions = new List<InstructionDefinition>
                        {
                            new InstructionDefinition { Mnemonic = "NOP", Description = "Do nothing", Operands = 0, Steps = { Step("ir.reset") } },
                            new InstructionDefinition
                            {
                                Mnemonic = "JMP", Description = "Jump to the address", Operands = 1, OperandTypes = new List<OperandType> { OperandType.Address },
                                Steps = { Step("pc.output", "mem.loadmar"), Step("mem.output", "pc.load", "ir.reset") },
                            },
                            new InstructionDefinition { Mnemonic = "HLT", Description = "Halt the machine", Operands = 0, Steps = { Step("clk.disable") } },
                        },
                    },
                },
                Halt = "clk",
                ProgramMemory = "mem",
            };
            machine.EnsureLayout();
            return new MachinePackage
            {
                Name = name,
                Description = "A new machine, started from the minimal CPU",
                Readme = StarterReadme(name, "It started from the minimal CPU: a program counter, memory, the instruction register, a status register and a clock on one bus, with a fetch routine and NOP, JMP and HLT."),
                Machine = machine,
            };
        }

        public static MachinePackage Empty(string name)
        {
            var machine = new MachineDefinition { Name = name, Buses = { new BusDefinition { Id = "main" } } };
            machine.EnsureLayout();
            return new MachinePackage { Name = name, Description = "A new, empty machine", Readme = StarterReadme(name, "It started empty: one bus and nothing else."), Machine = machine };
        }

        public static string StarterReadme(string name, string origin)
        {
            return $"# {name}\n\n{origin}\n\n## The idea\n\nWhat makes this machine different? Write it here.\n\n## The parts\n\n" +
                   "Its buses and devices, and what each one is for.\n\n## How an instruction runs\n\nWhat the fetch routine does, and the micro steps of a typical instruction.\n\n" +
                   "## Things to try\n\nPrograms to run, and what to watch in the Run view.";
        }

        public static MachinePackage CopyOf(MachinePackage package, string name)
        {
            var copy = package.Clone();
            copy.Name = name;
            copy.Description = $"A copy of {package.Name}";
            copy.Readme = string.IsNullOrWhiteSpace(package.Readme) ? StarterReadme(name, $"It started as a copy of {package.Name}.") : package.Readme;
            copy.Machine.Name = name;
            return copy;
        }
    }
}
