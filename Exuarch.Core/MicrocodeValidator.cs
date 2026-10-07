using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public enum DiagnosticSeverity { Error, Warning }

    // A problem in the microcode or a program: an error, or a warning.
    public interface IDiagnostic
    {
        DiagnosticSeverity Severity { get; }
    }

    public class MicrocodeDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }
        // Mnemonic the problem is in, or null for the microcode as a whole.
        public string Instruction { get; set; }
        // Index into the instruction's Steps list, or null.
        public int? Step { get; set; }
        public string Signal { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            var where = Instruction == null ? "Microcode" : Step == null ? $"Microcode {Instruction}" : $"Microcode {Instruction} step {Step + 1}";
            return $"{where}: {Message}";
        }
    }

    // Checks microcode against a machine definition: every signal must name a device and one of its
    // control lines, and no two signals in a step may drive the same bus. The checks are MicrocodeRules.
    public static class MicrocodeValidator
    {
        public static List<MicrocodeDiagnostic> Validate(MicrocodeDefinition microcode, MachineDefinition machine, DeviceRegistry registry = null, Machine built = null)
        {
            var context = new MicrocodeContext(microcode, machine, registry ?? DeviceRegistry.CreateDefault(), built);
            MicrocodeRules.Run(context);
            return context.Diagnostics;
        }
    }
}
