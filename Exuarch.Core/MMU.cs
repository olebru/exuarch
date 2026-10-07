using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class MMU : ControlLineDevice, IBusDevice, IObservableState, IBankedMemory
    {
        public Register ChipSelectRegister;
        public RamModule[] RamBanks;
        private Bus bus;
        private string deviceName;
        private string id;
        private bool select0Stack;
        // The bank lines enabled this tick, as bits numbered like the bank's own control lines: those that put a
        // value on the bus, and those that take one at the end of the tick.
        private int pendingDrives, pendingLatches;
        private static readonly string[] BankDriveLines = { "outputmar", "output" };
        public const int DefaultBanks = 16;
        public MMU(string DeviceName, string DeviceID, Bus bus, int banks = DefaultBanks, int bankSize = MemoryModule.DefaultSize)
        {
            if (banks < 1 || banks > 256) throw new ArgumentException($"An MMU has between 1 and 256 banks, not {banks}.");
            this.bus = bus;
            ChipSelectRegister = new Register("CS  ", "cs", this.bus);
            id = DeviceID;
            deviceName = DeviceName;
            RamBanks = new RamModule[banks];
            for (int i = 0; i < banks; i++)
            {
                RamBanks[i] = new RamModule($"Bank {i}", i.ToString(), this.bus, bankSize);
            }
            var bankLines = RamBanks[0].ControlLines;
            for (int line = 0; line < bankLines.Count; line++) ControlLines.Add(bankLines.Name(line), BankLine(bankLines.Name(line), 1 << line));
            ControlLines
                .Add("loadcs", ChipSelectRegister.ControlLines.Find("load"))
                .Add("outputcs", ChipSelectRegister.ControlLines.Find("output"))
                .Add("select0stack", () => select0Stack = true);
        }
        private Action BankLine(string name, int bit)
        {
            return Array.IndexOf(BankDriveLines, name) >= 0 ? () => pendingDrives |= bit : () => pendingLatches |= bit;
        }
        // The bank the chip select register points at; bank numbers wrap at the number of banks.
        public int SelectedBankNumber { get { return ChipSelectRegister.Data % RamBanks.Length; } }
        public RamModule SelectedBank { get { return RamBanks[SelectedBankNumber]; } }
        public IReadOnlyList<IWriteTracked> WriteTrackedBanks { get { return RamBanks; } }
        // select0stack applies before anything else. Bank outputs use the bank selected at the start of the
        // tick; bank inputs use the bank selected after the chip select register latched this tick.
        public void Drive()
        {
            if (select0Stack)
            {
                ChipSelectRegister.Data = 0;
                select0Stack = false;
            }
            ChipSelectRegister.Drive();
            var bank = SelectedBank;
            EnableOn(bank, pendingDrives);
            bank.Drive();
        }
        public void Latch()
        {
            ChipSelectRegister.Latch();
            var bank = SelectedBank;
            EnableOn(bank, pendingLatches);
            pendingDrives = pendingLatches = 0;
            bank.Latch();
        }
        private static void EnableOn(RamModule bank, int lines)
        {
            for (int line = 0; lines != 0; line++, lines >>= 1)
            {
                if ((lines & 1) != 0) bank.ControlLines[line]();
            }
        }
        public string DisplayName() { return deviceName; }
        public string ID()
        {
            return id;
        }
        public bool IsOutputEnabled()
        {
            return ChipSelectRegister.IsOutputEnabled() || pendingDrives != 0;
        }
        public void Observe(WatchValue watch)
        {
            watch(id + ".cs", () => ChipSelectRegister.Data);
            watch(id + ".mar", () => SelectedBank.memoryAddress);
        }
    }
}
