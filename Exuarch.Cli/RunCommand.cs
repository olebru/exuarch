using System.Collections.Generic;
using System.IO;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class RunCommand : ICommand
    {
        public const int DefaultTicks = 10_000_000;

        public string Name { get { return "run"; } }
        public string Usage { get { return "run <package> [--program <name or number> | --file <program.asm>] [--ticks <n>] [--memory <device>:<start>[:<count>]]..."; } }
        public string Summary { get { return "Run a program until it halts, then print displays, screens, registers and memory"; } }
        public IReadOnlyCollection<string> Options { get { return new[] { "program", "file", "ticks", "memory" }; } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(1);
            var package = PackageSource.Load(arguments.Positional(0, "package"));
            var program = PackageSource.Program(package, arguments);
            int limit = arguments.Number("ticks", DefaultTicks);
            var report = new Report(output);
            if (!AssembleCommand.Assemble(package, program.Source, report).Success) return ExitCode.Problems;
            var machine = new Machine(package.Machine, program.Source) { RecordHistory = false };
            var dumps = MachineState.MemoryDumps(machine, arguments.Options("memory"));
            report.Diagnostics(machine.MicrocodeWarnings);
            if (program.Needs?.Keypad == true) report.Line("note: this program reads the keypad; run cannot press keys, so it sees none held.");
            report.Line(Outcome(machine, Tick(machine, limit)));
            MachineState.Print(machine, dumps, output);
            return ExitCode.Success;
        }

        private static int Tick(Machine machine, int limit)
        {
            int ticks = 0;
            while (!machine.IsHalted && ticks < limit)
            {
                machine.SingleStep();
                ticks++;
            }
            return ticks;
        }

        private static string Outcome(Machine machine, int ticks)
        {
            if (machine.IsHalted) return $"Halted after {ticks:N0} ticks.";
            var at = machine.CurrentInstructionAddress is int address ? $" It was at {address:X4}{Source(machine, address)}." : "";
            return $"Still running after {ticks:N0} ticks.{at} Raise --ticks if it should halt, or it loops for ever.";
        }

        private static string Source(Machine machine, int address)
        {
            var text = machine.InstructionAt(address)?.Text?.Trim();
            return string.IsNullOrEmpty(text) ? "" : $": {text}";
        }
    }
}
