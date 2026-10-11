using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public delegate void WatchValue(string key, Func<int> read);

    public interface IObservableState
    {
        void Observe(WatchValue watch);
    }

    internal interface IBankedMemory
    {
        IReadOnlyList<IWriteTracked> WriteTrackedBanks { get; }
    }

    internal interface IRegisterBank
    {
        int Count { get; }
    }
}
