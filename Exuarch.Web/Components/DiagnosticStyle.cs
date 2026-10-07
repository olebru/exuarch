using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Components
{
    // How problems look and add up, in the microcode and in a program: an error in the default colours, a warning
    // marked "warning".
    public static class DiagnosticStyle
    {
        public static string For(DiagnosticSeverity severity) => severity == DiagnosticSeverity.Warning ? "warning" : "";

        // The worst of some problems: "error", "warning", or "" for none.
        public static string Worst(IEnumerable<IDiagnostic> diagnostics)
        {
            var list = diagnostics.ToList();
            if (list.Any(d => d.Severity == DiagnosticSeverity.Error)) return "error";
            return list.Count > 0 ? "warning" : "";
        }

        public static int Errors(IEnumerable<IDiagnostic> diagnostics) => diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        public static int Warnings(IEnumerable<IDiagnostic> diagnostics) => diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
    }
}
