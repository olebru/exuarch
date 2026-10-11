using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    public sealed class DecoderView
    {
        private const int FetchBlockIndex = 0;

        private readonly RunSession session;
        private bool wholeRom;

        public DecoderView(RunSession session)
        {
            this.session = session;
        }

        private Machine Machine => session.Machine;
        private TickRecord Last => Machine.LastTick;

        public bool WholeRom
        {
            get => wholeRom;
            set
            {
                wholeRom = value;
                session.NotifyChanged();
            }
        }

        public int Status => (Last?.Status ?? Machine.NextDecoderStatus) & DecoderRom.StatusMask;
        public int Step => Last?.MicroStep ?? Machine.MicroStepRegister;
        public int Address => DecoderRom.RomAddress(Status, Step);
        public int NextAddress => DecoderRom.RomAddress(Machine.NextDecoderStatus, Machine.MicroStepRegister);

        public List<RomRow> Rows()
        {
            var rom = Machine.DecoderRom;
            int status = Status, step = Step;
            var rows = new List<RomRow>();
            foreach (var block in rom.Blocks.Where((b, i) => wholeRom || i == FetchBlockIndex || BlockContains(b.Base, b.Count, step)))
            {
                for (int offset = 0; offset < block.Count; offset++) rows.Add(Row(rom, status, block.Base + offset, $"{block.Instruction.Mnemonic}.{offset + 1}", offset == 0));
            }
            return rows;
        }

        private static bool BlockContains(int blockBase, int count, int step) => step >= blockBase && step < blockBase + count;

        private RomRow Row(DecoderRom rom, int status, int address, string label, bool blockStart)
        {
            bool current = Last != null && address == Step;
            var signals = new HashSet<string>(rom.FetchInstruction(status, address).Select(m => $"{m.DeviceID}.{m.Function}"));
            return new RomRow(DecoderRom.RomAddress(status, address), label, $"{(current ? "current" : "")} {(blockStart ? "block-start" : "")}", signals);
        }

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

    public sealed record RomRow(int RomAddress, string Label, string Css, HashSet<string> Signals);

    public sealed record RomColumn(string Line, string Signal, string Color, string HeadCss, string CellCss);
}
