using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // A directive: how the assembler stores its operands and what the editor says about it. Each function takes the
    // directive as it is written.
    internal sealed class AssemblyDirective
    {
        public string Name { get; init; }
        // Cells stored after the operands: .STRING ends its text with a 0.
        public int[] Terminator { get; init; } = Array.Empty<int>();
        public Func<string, string> Hover { get; init; }
        // Reported where the directive is used, or null when it is fine to use.
        public Func<string, string> Problem { get; init; }
        public DiagnosticSeverity Severity { get; init; }
        // Offered while typing a mnemonic.
        public string Detail { get; init; }
        public string Documentation { get; init; }
        public string InsertText { get; init; }

        public CompletionItem Completion()
        {
            return new CompletionItem { Label = Name, Kind = CompletionKind.Directive, Detail = Detail, Documentation = Documentation, InsertText = InsertText };
        }

        public void Report(DiagnosticBag diagnostics, ParsedLine line)
        {
            if (Problem != null) diagnostics.Add(Severity, line, line.Mnemonic, Problem(line.Mnemonic.Text));
        }
    }

    // Every directive the assembler knows, read in any case.
    internal static class AssemblyDirectives
    {
        public static readonly AssemblyDirective Data = new AssemblyDirective
        {
            Name = ".DATA",
            Hover = _ => "**.DATA** values, labels, \"strings\"\n\nStores each value, label address or character of a string in its own 16 bit memory cell.",
            Detail = "data",
            Documentation = "Store values, labels or \"strings\" in memory, one 16 bit cell each",
            InsertText = ".DATA ",
        };

        public static readonly AssemblyDirective String = new AssemblyDirective
        {
            Name = Assembler.StringDirective,
            Terminator = new[] { 0 },
            Hover = _ => "**.STRING** \"text\", values\n\nLike .DATA, then a 0 cell, so a program can find where the string ends.",
            Detail = "text",
            Documentation = "Store \"text\" and values like .DATA, followed by a 0 cell that ends the string",
            InsertText = ".STRING \"",
        };

        // Stores like .DATA, so the operands still make cells.
        public static readonly AssemblyDirective Unknown = new AssemblyDirective
        {
            Hover = written => $"**{written}** is not a directive. Use .DATA or .STRING.",
            Problem = written => $"unknown directive '{written}', use .DATA or .STRING",
            Severity = DiagnosticSeverity.Error,
        };

        public static readonly IReadOnlyList<AssemblyDirective> Current = new[] { Data, String };

        // Older names for .DATA: both always stored one value per cell. They still assemble, with a warning.
        public static readonly IReadOnlyList<AssemblyDirective> OldNames = new[] { OldNameForData(".BYTE"), OldNameForData(".WORD") };

        private static readonly Dictionary<string, AssemblyDirective> byName = Current.Concat(OldNames).ToDictionary(d => d.Name);

        public static AssemblyDirective Find(string written)
        {
            return byName.TryGetValue(written.ToUpperInvariant(), out var directive) ? directive : Unknown;
        }

        private static AssemblyDirective OldNameForData(string name)
        {
            return new AssemblyDirective
            {
                Name = name,
                Hover = written => $"**{written}** is an old name for **.DATA**: every value takes one 16 bit cell either way.",
                Problem = _ => $"{name} is an old name for .DATA: every value takes one 16 bit cell either way. Use .DATA, or .STRING for text that ends with 0.",
                Severity = DiagnosticSeverity.Warning,
            };
        }
    }
}
