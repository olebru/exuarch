using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // The decoder ROM panel: the address the decoder reads, made of the status and the micro step register, and the
    // ROM rows around it, a column for every control line.
    public sealed class DecoderView
    {
        private readonly RunSession session;
        private bool wholeRom;

        public DecoderView(RunSession session)
        {
            this.session = session;
        }

        private Machine Machine => session.Machine;
        private TickRecord Last => Machine.LastTick;

        // Every block of the ROM, rather than the fetch block and the one the decoder just used.
        public bool WholeRom
        {
            get => wholeRom;
            set
            {
                wholeRom = value;
                session.NotifyChanged();
            }
        }

        // The address the last tick read, or the next tick reads before the first.
        public int Status => (Last?.Status ?? Machine.NextDecoderStatus) & DecoderRom.StatusMask;
        public int Step => Last?.MicroStep ?? Machine.MicroStepRegister;
        public int Address => DecoderRom.RomAddress(Status, Step);
        public int NextAddress => DecoderRom.RomAddress(Machine.NextDecoderStatus, Machine.MicroStepRegister);

        // ROM rows to show: the fetch block and the block the decoder just used, or the whole ROM.
        public List<RomRow> Rows()
        {
            var rom = Machine.DecoderRom;
            int status = Status, step = Step;
            var rows = new List<RomRow>();
            foreach (var block in rom.Blocks.Where((b, i) => wholeRom || i == 0 || (step >= b.Base && step < b.Base + b.Count)))
            {
                for (int offset = 0; offset < block.Count; offset++) rows.Add(Row(rom, status, block.Base + offset, $"{block.Instruction.Mnemonic}.{offset + 1}", offset == 0));
            }
            return rows;
        }

        private RomRow Row(DecoderRom rom, int status, int address, string label, bool blockStart)
        {
            bool current = Last != null && address == Step;
            var signals = new HashSet<string>(rom.FetchInstruction(status, address).Select(m => $"{m.DeviceID}.{m.Function}"));
            return new RomRow(DecoderRom.RomAddress(status, address), label, $"{(current ? "current" : "")} {(blockStart ? "block-start" : "")}", signals);
        }

        // A column for every control line, in the strip's order, the first of each device's marked.
        public static List<RomColumn> Columns(List<ControlLineGroup> groups, LastTickView last)
        {
            return groups.SelectMany(group => group.Lines.Select((line, i) => new RomColumn(
                line,
                $"{group.Device.Id}.{line}",
                group.Color,
                $"rom-line {(last.LineActive(group.Device.Id, line) ? "on" : "")} {(i == 0 ? "first" : "")}",
                $"rom-cell {(i == 0 ? "first" : "")}"))).ToList();
        }
    }

    // A ROM row: its full address, the instruction step it holds, and the control lines it turns on.
    public sealed record RomRow(int RomAddress, string Label, string Css, HashSet<string> Signals);

    public sealed record RomColumn(string Line, string Signal, string Color, string HeadCss, string CellCss);
}
