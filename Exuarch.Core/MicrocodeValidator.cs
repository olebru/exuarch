using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public enum DiagnosticSeverity { Error, Warning }

    public interface IDiagnostic
    {
        DiagnosticSeverity Severity { get; }
    }

    public class MicrocodeDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }
        public string Instruction { get; set; }
        public int? Step { get; set; }
        public string Signal { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            var where = Instruction == null ? "Microcode" : Step == null ? $"Microcode {Instruction}" : $"Microcode {Instruction} step {Step + 1}";
            return $"{where}: {Message}";
        }
    }

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
