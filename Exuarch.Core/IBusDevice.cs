using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // A clock tick has two phases: every device first drives its outputs onto its buses (Drive),
    // then every device latches its inputs (Latch). Device order therefore never matters.
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
