using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public static class RomContents
    {
        private const int LargestMemory = 65536;

        public static int SizeOf(DeviceDefinition device)
        {
            return device.Parameters.TryGetValue("size", out var size) && size.TryGetInt32(out var cells) && cells >= 1 && cells <= LargestMemory ? cells : MemoryModule.DefaultSize;
        }

        public static string Source(IEnumerable<string> lines)
        {
            return string.Join("\n", lines ?? Enumerable.Empty<string>());
        }

        public static List<string> Lines(string source)
        {
            var lines = SourceText.SplitLines(source ?? "").ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        public static AssemblyResult Analyze(string source, int size)
        {
            var result = new Assembler(_ => null, _ => null, LargestMemory).Analyze(source ?? "");
            var instructions = result.Lines.Where(l => l.Mnemonic != null && !l.IsDirective).ToDictionary(l => l.Number, l => l.Mnemonic.Text);
            foreach (var diagnostic in result.Diagnostics.Where(d => instructions.ContainsKey(d.Line) && d.Message.StartsWith("unknown mnemonic")))
            {
                diagnostic.Message = $"a ROM holds data, not instructions: write .DATA or .STRING instead of '{instructions[diagnostic.Line]}'";
            }
            foreach (var line in result.Listing.Where(l => !string.IsNullOrEmpty(l.Label))) result.Diagnostics.Add(LabelNotAllowed(line));
            if (result.Cells.Length > size) result.Diagnostics.Add(TooLarge(result, size));
            return result;
        }

        private static AssemblyDiagnostic LabelNotAllowed(ListingLine line)
        {
            var text = line.Text ?? "";
            int start = Math.Max(0, text.IndexOf(line.Label, StringComparison.Ordinal));
            int end = text.IndexOf(':', start);
            return new AssemblyDiagnostic
            {
                Line = line.LineNumber,
                StartColumn = start + 1,
                EndColumn = (end >= 0 ? end + 1 : start + line.Label.Length) + 1,
                Message = $"a ROM has no labels: '{line.Label}' would name address {line.Address}, which the margin already shows",
                Text = text.Trim(),
            };
        }

        public static string[] Margin(AssemblyResult result, int size)
        {
            var format = size > 256 ? "X4" : "X2";
            int lines = result.Listing.Select(l => l.LineNumber).DefaultIfEmpty(0).Max();
            var margin = Enumerable.Repeat("", lines).ToArray();
            foreach (var line in result.Listing.Where(l => l.Cells.Length > 0 && l.LineNumber >= 1)) margin[line.LineNumber - 1] = line.Address.ToString(format);
            return margin;
        }

        private static AssemblyDiagnostic TooLarge(AssemblyResult result, int size)
        {
            var last = result.Listing.LastOrDefault(l => l.Cells.Length > 0);
            return new AssemblyDiagnostic
            {
                Line = last?.LineNumber ?? 1,
                StartColumn = 1,
                EndColumn = (last?.Text?.Length ?? 0) + 1,
                Message = $"the contents are {result.Cells.Length} cells, but the ROM only holds {size}",
                Text = last?.Text?.Trim() ?? "",
            };
        }

        public static int[] Cells(IEnumerable<string> lines, int size)
        {
            var result = Analyze(Source(lines), size);
            var error = result.Errors.FirstOrDefault();
            if (error != null) throw new FormatException(error.ToString());
            return result.Cells;
        }
    }
}
