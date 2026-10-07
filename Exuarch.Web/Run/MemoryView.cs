using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
    // The memory panel: which memory it shows, which bank of an MMU, and which page of PageSize cells.
    public sealed class MemoryView
    {
        public const int PageSize = 256;

        private readonly RunSession session;
        private int bank;
        private int page;

        public MemoryView(RunSession session)
        {
            this.session = session;
        }

        private Machine Machine => session.Machine;

        // The memory shown, by device id.
        public string DeviceId { get; private set; }
        public IEnumerable<IBusDevice> Devices => Machine.Devices.Where(d => d is MemoryModule || d is MMU);
        public MMU Mmu => Machine.Device(DeviceId) as MMU;

        // The cells shown: the memory module's, or the bank of the MMU asked for, or the nearest there is.
        public MemoryModule Module => Machine.Device(DeviceId) switch
        {
            MMU mmu => mmu.RamBanks[Math.Clamp(bank, 0, mmu.RamBanks.Length - 1)],
            MemoryModule rom => rom,
            _ => null,
        };

        // The MMU bank asked for.
        public int Bank
        {
            get => bank;
            set
            {
                bank = value;
                session.NotifyChanged();
            }
        }

        // The bank the writes shown were made to: the one asked for on an MMU, and -1, none, on other memory.
        public int WriteBank => Mmu != null ? bank : -1;

        public int PageCount(MemoryModule module) => (module.Size + PageSize - 1) / PageSize;
        public int Page(MemoryModule module) => Math.Clamp(page, 0, PageCount(module) - 1);

        public void Select(string deviceId)
        {
            DeviceId = deviceId;
            session.NotifyChanged();
        }

        public void TurnPage(MemoryModule module, int pages)
        {
            page = Page(module) + pages;
            session.NotifyChanged();
        }

        // The page with the cell the memory address register points at.
        public void GoToMar(MemoryModule module)
        {
            page = module.memoryAddress / PageSize;
            session.NotifyChanged();
        }

        // A new machine keeps the memory shown when it has it too, and otherwise shows its program memory.
        public void PickFor(Machine machine)
        {
            if (DeviceId != null && machine.Device(DeviceId) != null) return;
            DeviceId = machine.Definition.ProgramMemory ?? machine.Devices.FirstOrDefault(d => d is MemoryModule || d is MMU)?.ID();
        }

        // Enough hex digits for the module's highest address, and at least two.
        public static int AddressDigits(MemoryModule module) => Math.Max(2, ((int)Math.Ceiling(Math.Log2(Math.Max(2, module.Size))) + 3) / 4);

        public MemoryMarks Marks() => MemoryMarks.Build(Machine, DeviceId, WriteBank);
    }
}
