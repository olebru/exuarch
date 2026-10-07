using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Workbench
{
    // What checking the workspace found, stage by stage: the machine's JSON, its hardware, its microcode against the
    // hardware, and the program, which is only assembled once everything before it is right. The machine is built when
    // every stage passes.
    public sealed record ValidationResult(
        IReadOnlyList<string> ParseErrors,
        IReadOnlyList<string> DefinitionErrors,
        IReadOnlyList<MicrocodeDiagnostic> MicrocodeDiagnostics,
        IReadOnlyList<string> ProgramErrors,
        Machine Machine)
    {
        private static readonly MicrocodeDiagnostic[] NoMicrocode =
        {
            new MicrocodeDiagnostic { Severity = DiagnosticSeverity.Error, Message = "the machine has no microcode (decoder.microcode)." },
        };

        public static readonly ValidationResult None = new ValidationResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<MicrocodeDiagnostic>(), Array.Empty<string>(), null);

        public int MicrocodeErrorCount => DiagnosticStyle.Errors(MicrocodeDiagnostics);
        public int MicrocodeWarningCount => DiagnosticStyle.Warnings(MicrocodeDiagnostics);

        // The first stage that failed: "hardware" (the JSON or the hardware), "microcode" or "program"; null when the
        // machine builds.
        public string FailingStage
        {
            get
            {
                if (ParseErrors.Count > 0 || DefinitionErrors.Count > 0) return "hardware";
                if (MicrocodeErrorCount > 0) return "microcode";
                return ProgramErrors.Count > 0 ? "program" : null;
            }
        }

        public IEnumerable<string> AllErrors =>
            ParseErrors.Concat(DefinitionErrors)
                .Concat(MicrocodeDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
                .Concat(ProgramErrors);

        // Validates the definition, then the microcode against it, then builds a machine to assemble the program.
        public static ValidationResult Of(MachineDefinition definition, string program, IReadOnlyList<string> parseErrors, DeviceRegistry registry)
        {
            var microcode = definition.Decoder?.Microcode;
            var checkedSoFar = None with
            {
                ParseErrors = parseErrors,
                DefinitionErrors = Machine.ValidateDefinition(definition, registry),
                MicrocodeDiagnostics = microcode == null ? NoMicrocode : MicrocodeValidator.Validate(microcode, definition, registry),
            };
            return checkedSoFar.FailingStage == null ? checkedSoFar.Build(definition, program, registry) : checkedSoFar;
        }

        private ValidationResult Build(MachineDefinition definition, string program, DeviceRegistry registry)
        {
            try
            {
                return this with { Machine = new Machine(definition.Clone(), program, registry) };
            }
            catch (MachineDefinitionException e)
            {
                return this with { ProgramErrors = e.Errors.ToList() };
            }
            catch (Exception e)
            {
                return this with { ProgramErrors = new[] { e.Message } };
            }
        }

        // The JSON did not parse: the last good machine is kept in the editors, but it does not run.
        public ValidationResult WithParseErrors(IReadOnlyList<string> errors) => this with { ParseErrors = errors, Machine = null };
    }
}
