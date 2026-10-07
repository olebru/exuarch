using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // Names one value of a device the machine watches, such as "mem.mar", and how to read it.
    public delegate void WatchValue(string key, Func<int> read);

    // A device whose values the machine compares before and after each recorded tick, to list what changed. A
    // device names each value once; the machine reads them as often as it needs.
    public interface IObservableState
    {
        void Observe(WatchValue watch);
    }

    // A device made of memories whose stores are recorded one by one, by bank number: an MMU's banks.
    internal interface IBankedMemory
    {
        IReadOnlyList<IWriteTracked> WriteTrackedBanks { get; }
    }

    // A device holding the registers that register operands name (R0, R1, ...).
    internal interface IRegisterBank
    {
        int Count { get; }
    }
}
