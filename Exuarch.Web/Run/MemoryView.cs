using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Web.Run
{
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

        public string DeviceId { get; private set; }
        public IEnumerable<IBusDevice> Devices => Machine.Devices.Where(d => d is MemoryModule || d is MMU);
        public MMU Mmu => Machine.Device(DeviceId) as MMU;

        public MemoryModule Module => Machine.Device(DeviceId) switch
        {
            MMU mmu => mmu.RamBanks[Math.Clamp(bank, 0, mmu.RamBanks.Length - 1)],
            MemoryModule rom => rom,
            _ => null,
        };

        public int Bank
        {
            get => bank;
            set
            {
                bank = value;
                session.NotifyChanged();
            }
        }

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

        public void GoToMar(MemoryModule module)
        {
            page = module.memoryAddress / PageSize;
            session.NotifyChanged();
        }

        public void PickFor(Machine machine)
        {
            if (DeviceId != null && machine.Device(DeviceId) != null) return;
            DeviceId = machine.Definition.ProgramMemory ?? machine.Devices.FirstOrDefault(d => d is MemoryModule || d is MMU)?.ID();
        }

        public static int AddressDigits(MemoryModule module) => Math.Max(2, ((int)Math.Ceiling(Math.Log2(Math.Max(2, module.Size))) + 3) / 4);

        public MemoryMarks Marks() => MemoryMarks.Build(Machine, DeviceId, WriteBank);
    }
}
