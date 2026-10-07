using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using static Exuarch.Web.Components.Formats;

namespace Exuarch.Web.Run
{
    // A line of the program listing as the Program panel shows it. The current instruction's line has the id the view
    // scrolls to; an instruction's line can take a breakpoint, a data line can not.
    public sealed record ListingRow(string Id, string Css, string Title, int? Breakpoint, int Address, string Label, string Source)
    {
        public static IEnumerable<ListingRow> Of(Machine machine)
        {
            return machine.Assembler.Listing.Select(line => Of(line, machine.CurrentInstructionAddress));
        }

        // The label column is as wide as the longest label and its colon, so the code after it lines up.
        public static int LabelChars(Machine machine)
        {
            return machine.Assembler.Listing.Select(l => (l.Label?.Length ?? 0) + 1).DefaultIfEmpty(1).Max();
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
                line.Label == null ? "" : line.Label + ":",
                SourceOf(line));
        }

        private static string SourceOf(ListingLine line)
        {
            if (line.IsInstruction) return RunText.SourceText(line);
            return line.Mnemonic == null ? "" : $"{line.Mnemonic} {string.Join(", ", line.Operands)}";
        }

        // What the line assembled to, shown on hover so the code has the width.
        private static string CellsTitle(ListingLine line)
        {
            if (line.Cells.Length == 0) return null;
            return $"{Hex(line.Address)}: {string.Join(" ", line.Cells.Select(b => Hex(b)))}";
        }
    }
}
