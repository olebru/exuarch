using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    internal sealed class DiagnosticBag
    {
        private readonly List<AssemblyDiagnostic> diagnostics;

        public DiagnosticBag(List<AssemblyDiagnostic> diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        public void Error(ParsedLine line, SourceToken token, string message)
        {
            Add(DiagnosticSeverity.Error, line, token, message);
        }

        public void Warning(ParsedLine line, SourceToken token, string message)
        {
            Add(DiagnosticSeverity.Warning, line, token, message);
        }

        public void Add(DiagnosticSeverity severity, ParsedLine line, SourceToken token, string message)
        {
            diagnostics.Add(new AssemblyDiagnostic
            {
                Severity = severity,
                Line = line.Number,
                StartColumn = token.Start,
                EndColumn = Math.Max(token.End, token.Start + 1),
                Message = message,
                Text = line.Text,
            });
        }

        public void AddRange(IEnumerable<AssemblyDiagnostic> found)
        {
            diagnostics.AddRange(found);
        }

        public void Sort()
        {
            diagnostics.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.StartColumn.CompareTo(b.StartColumn));
        }
    }
}
