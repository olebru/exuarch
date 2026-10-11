using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using static Exuarch.Web.Components.Formats;

namespace Exuarch.Web.Run
{
    public sealed record ListingRow(string Id, string Css, string Title, int? Breakpoint, int Address, string Label, string Source)
    {
        private const string LabelColon = ":";

        public static IEnumerable<ListingRow> Of(Machine machine)
        {
            return machine.Assembler.Listing.Select(line => Of(line, machine.CurrentInstructionAddress));
        }

        public static int LabelChars(Machine machine)
        {
            return machine.Assembler.Listing.Select(l => (l.Label?.Length ?? 0) + LabelColon.Length).DefaultIfEmpty(LabelColon.Length).Max();
        }

        private static ListingRow Of(ListingLine line, int? currentAddress)
        {
            bool current = line.IsInstruction && line.Address == currentAddress;
            return new ListingRow(
                "listing-" + (current ? line.Address.ToString() : ""),
                $"lst {(current ? "current" : "")} {(line.IsInstruction ? "" : "data")}",
                CellsTitle(line),
                line.IsInstruction ? line.Address : null,
                line.Address,
                line.Label == null ? "" : line.Label + LabelColon,
                SourceOf(line));
        }

        private static string SourceOf(ListingLine line)
        {
            if (line.IsInstruction) return RunText.SourceText(line);
            return line.Mnemonic == null ? "" : $"{line.Mnemonic} {string.Join(", ", line.Operands)}";
        }

        private static string CellsTitle(ListingLine line)
        {
            if (line.Cells.Length == 0) return null;
            return $"{Hex(line.Address)}: {string.Join(" ", line.Cells.Select(b => Hex(b)))}";
        }
    }
}
