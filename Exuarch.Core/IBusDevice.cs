using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public interface IBusDevice
    {
        void Drive();
        void Latch();
        string DisplayName();
        void Enable(string function);
        string ID();
        bool IsOutputEnabled();
        List<string> SignalLines();
    }

    public interface IPassiveDevice
    {
    }
}
