using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Microcoding
{
    public partial class MicrocodeEditor
    {
        [Inject] private DeviceRegistry Registry { get; set; }

        [Parameter] public MicrocodeDefinition Microcode { get; set; }
        [Parameter] public EventCallback<MicrocodeDefinition> MicrocodeChanged { get; set; }
        [Parameter] public MachineDefinition Machine { get; set; }
        [Parameter] public IReadOnlyList<MicrocodeDiagnostic> Diagnostics { get; set; } = Array.Empty<MicrocodeDiagnostic>();
        [Parameter] public EditHistory History { get; set; }
        [Parameter] public EventCallback<string> OnOpenDevice { get; set; }
        [Parameter] public FocusRequest Focus { get; set; } = FocusRequest.None;

        private readonly MicrocodeSession session;
        private bool showProblems;
        private bool showFormat;

        public MicrocodeEditor()
        {
            session = new MicrocodeSession(microcode => MicrocodeChanged.InvokeAsync(microcode), deviceId => OnOpenDevice.InvokeAsync(deviceId));
            session.StateChanged += StateHasChanged;
        }

        protected override void OnParametersSet()
        {
            session.Update(Microcode, Machine, Diagnostics, Registry, History, Focus);
        }

        private int OpCodesUsed => OpcodeLayout.Used(session.Microcode);
        private int SmallestVisibleMeterPercent => OpCodesUsed > 0 ? 1 : 0;
        private int MeterPercent => Math.Max(SmallestVisibleMeterPercent, Math.Min(100, OpCodesUsed * 100 / OpcodeLayout.AddressSpace));
    }
}
