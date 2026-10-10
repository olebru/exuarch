using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class Report
    {
        private readonly TextWriter output;
        public int Errors { get; private set; }
        public int Warnings { get; private set; }

        public Report(TextWriter output) { this.output = output; }

        public void Line(string text) { output.WriteLine(text); }
        public void Error(string text) { Errors++; output.WriteLine($"error: {text}"); }
        public void Warning(string text) { Warnings++; output.WriteLine($"warning: {text}"); }

        public void Diagnostics(IEnumerable<IDiagnostic> diagnostics, string prefix = "")
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error) Error(prefix + diagnostic);
                else Warning(prefix + diagnostic);
            }
        }

        public void Summary()
        {
            output.WriteLine($"{Plural(Errors, "error")}, {Plural(Warnings, "warning")}.");
        }

        public static string Plural(int count, string noun, string nouns = null) { return count == 1 ? $"1 {noun}" : $"{count} {nouns ?? noun + "s"}"; }
    }

    public static class Build
    {
        public static Machine Empty(MachinePackage package)
        {
            return new Machine(package.Machine, "") { RecordHistory = false };
        }

        public static string Describe(MachinePackage package)
        {
            var machine = package.Machine;
            var instructions = machine.Decoder?.Microcode?.AllInstructions.Count() ?? 0;
            return $"{package.Name}: {Report.Plural(machine.Devices.Count, "device")} on {Report.Plural(machine.Buses.Count, "bus", "buses")}, {Report.Plural(instructions, "instruction")}, {Report.Plural(package.Programs.Count, "program")}.";
        }
    }
}
