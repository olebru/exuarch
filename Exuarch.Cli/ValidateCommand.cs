using System;
using System.Collections.Generic;
using System.IO;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class ValidateCommand : ICommand
    {
        public string Name { get { return "validate"; } }
        public string Usage { get { return "validate <package>"; } }
        public string Summary { get { return "Check the machine, its microcode and every program in the package"; } }
        public IReadOnlyCollection<string> Options { get { return Array.Empty<string>(); } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(1);
            var package = PackageSource.Load(arguments.Positional(0, "package"));
            var report = new Report(output);
            var machine = TryBuild(package, report);
            if (machine != null) CheckPrograms(package, machine, report);
            report.Summary();
            return report.Errors > 0 ? ExitCode.Problems : ExitCode.Success;
        }

        private static Machine TryBuild(MachinePackage package, Report report)
        {
            try
            {
                var machine = Build.Empty(package);
                report.Line(Build.Describe(package));
                report.Diagnostics(machine.MicrocodeWarnings);
                return machine;
            }
            catch (MachineDefinitionException e)
            {
                foreach (var problem in e.Errors) report.Error(problem);
                return null;
            }
        }

        private static void CheckPrograms(MachinePackage package, Machine machine, Report report)
        {
            for (int i = 0; i < package.Programs.Count; i++)
            {
                var program = package.Programs[i];
                var result = machine.Assembler.Analyze(program.Source ?? "");
                var label = $"program {i + 1} \"{program.Name}\"";
                if (result.Success) report.Line($"{label}: {Report.Plural(result.Cells.Length, "word")}");
                report.Diagnostics(result.Diagnostics, $"{label}: ");
            }
        }
    }
}
