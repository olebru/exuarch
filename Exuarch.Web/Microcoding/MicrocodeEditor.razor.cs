using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.Microcoding
{
    // The microcode editor: the list of instructions, the steps of the one picked and the palette of control lines.
    // Where the editor is and every edit are in MicrocodeSession; this puts it on the page and reads and saves files.
    public partial class MicrocodeEditor
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }

        [Parameter] public MicrocodeDefinition Microcode { get; set; }
        [Parameter] public EventCallback<MicrocodeDefinition> MicrocodeChanged { get; set; }
        [Parameter] public MachineDefinition Machine { get; set; }
        [Parameter] public IReadOnlyList<MicrocodeDiagnostic> Diagnostics { get; set; } = Array.Empty<MicrocodeDiagnostic>();
        // Undo history shared with the machine editor.
        [Parameter] public EditHistory History { get; set; }
        // Asks the page to show a device in the machine editor.
        [Parameter] public EventCallback<string> OnOpenDevice { get; set; }
        // Picks an instruction and step when another view points at it.
        [Parameter] public FocusRequest Focus { get; set; } = FocusRequest.None;

        private readonly MicrocodeSession session;
        private string importError;
        private bool showProblems;

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
        // How full the micro step address space is, in percent; a sliver as soon as anything is in it.
        private int MeterPercent => Math.Max(OpCodesUsed > 0 ? 1 : 0, Math.Min(100, OpCodesUsed * 100 / OpcodeLayout.AddressSpace));

        private async Task Download()
        {
            var name = string.IsNullOrWhiteSpace(session.Microcode.Name) ? "microcode" : session.Microcode.Name;
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{name}.microcode.json", session.Microcode.ToJson());
        }

        private async Task Import(InputFileChangeEventArgs e)
        {
            importError = null;
            try
            {
                using var reader = new System.IO.StreamReader(e.File.OpenReadStream(2 * 1024 * 1024));
                await session.Replace(MicrocodeDefinition.Parse(await reader.ReadToEndAsync()));
            }
            catch (Exception ex) when (ex is MachineDefinitionException || ex is FormatException || ex is System.IO.IOException)
            {
                importError = ex.Message;
            }
        }
    }
}
