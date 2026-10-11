using System.Collections.Generic;
namespace Exuarch.Core
{
    public class TickRecord
    {
        public int Cycle { get; set; }
        public string Instruction { get; set; }
        public int? StepIndex { get; set; }
        public int Status { get; set; }
        public int MicroStep { get; set; }
        public int RomAddress { get; set; }
        public List<string> Signals { get; set; } = new List<string>();
        public List<BusTransfer> Transfers { get; set; } = new List<BusTransfer>();
        public List<ValueChange> Changes { get; set; } = new List<ValueChange>();
        public List<MemoryWrite> Writes { get; set; } = new List<MemoryWrite>();
        public int? FetchedFromAddress { get; set; }
    }

    public class BusTransfer
    {
        public string Bus { get; set; }
        public string Driver { get; set; }
        public int Value { get; set; }
        public List<string> Readers { get; set; } = new List<string>();
    }

    public class ValueChange
    {
        public string Device { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
    }

    public class MemoryWrite
    {
        public string Device { get; set; }
        public const int NoBank = -1;
        public int Bank { get; set; } = NoBank;
        public int Address { get; set; }
        public int Value { get; set; }
    }
}
