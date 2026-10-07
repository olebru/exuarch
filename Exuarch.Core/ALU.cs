using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    // 16 bit ALU working on two registers (a and b) and writing its flags to a status register. Every operation
    // sets Z when the result is 0 and N from its top bit, the sign in two's complement.
    //   add: a + b. C when the sum carries out, V on signed overflow.
    //   sub: a - b; cmp sets the same flags without driving the bus. C when a < b unsigned (a borrow), V on signed
    //        overflow. So after cmp: Z equal, C below (unsigned), N != V less than (signed).
    //   and, orr, eor: bitwise.
    //   lsl, lsr: a shifted left or right by b (0-15). C the last bit shifted out.
    // Each operation is a control line of its own, and only one can run in a tick.
    public class ALU : ControlLineDevice, IBusDevice
    {
        private Register a;
        private Register b;
        private Bus bus;
        private string deviceID;
        private string deviceName;
        private Register sta;
        private AluOperation pending;
        private int? pendingStatus;
        public ALU(string DeviceName, string DeviceID, Register rega, Register regb, Register regsta, Bus Bus)
        {
            deviceID = DeviceID;
            a = rega;
            b = regb;
            sta = regsta;
            bus = Bus;
            deviceName = DeviceName;
            foreach (var operation in AluOperation.All) ControlLines.Add(operation.Name, () => Start(operation));
        }
        private void Start(AluOperation operation)
        {
            if (pending != null && pending != operation) throw new Exception($"{deviceID}: '{pending.Name}' and '{operation.Name}' can not run in the same tick.");
            pending = operation;
        }
        // Operands are read in the drive phase, before any register latches a new value this tick.
        public void Drive()
        {
            if (pending == null) return;
            int result = pending.Run(a.Data, b.Data, out int status);
            if (pending.DrivesBus) bus.Data = result;
            pendingStatus = status;
            pending = null;
        }
        public void Latch()
        {
            if (pendingStatus.HasValue)
            {
                sta.Data = pendingStatus.Value;
                pendingStatus = null;
            }
        }
        public string DisplayName() { return deviceName; }
        public string ID() { return deviceID; }
        public bool IsOutputEnabled()
        {
            return pending != null && pending.DrivesBus;
        }
    }
}
