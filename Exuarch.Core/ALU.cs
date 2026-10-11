using System;
using System.Collections.Generic;
namespace Exuarch.Core
{
    public class ALU : ControlLineDevice, IBusDevice, IPassiveDevice
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
