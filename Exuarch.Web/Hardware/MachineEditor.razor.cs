using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.Hardware
{
    // The hardware design: the palette, the canvas and the inspector around the machine being designed. What is edited
    // and selected is in MachineDesign, the pointer on the canvas in CanvasInteraction; this puts them on the page and
    // does what needs the browser.
    public partial class MachineEditor
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }

        [Parameter] public MachineDefinition Definition { get; set; }
        [Parameter] public EventCallback<MachineDefinition> DefinitionChanged { get; set; }
        [Parameter] public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        [Parameter] public Machine Preview { get; set; }
        // Undo history shared with the microcode editor.
        [Parameter] public EditHistory History { get; set; }
        // Asks the page to show an instruction step in the microcode editor.
        [Parameter] public EventCallback<(string Mnemonic, int Step)> OnOpenMicrocode { get; set; }
        // Selects a device when another view points at it.
        [Parameter] public FocusRequest Focus { get; set; } = FocusRequest.None;

        private readonly MachineDesign design;
        private readonly CanvasInteraction interaction = new CanvasInteraction();
        private readonly KeyMap keys;
        private DesignCanvas canvas;
        private FocusRequest focusSeen = FocusRequest.None;
        private TypeTip tip;
        private bool showProblems;

        public MachineEditor()
        {
            design = new MachineDesign(definition => DefinitionChanged.InvokeAsync(definition));
            design.StateChanged += StateHasChanged;
            keys = new KeyMap()
                .On("Mod+Shift+Z", design.Redo)
                .On("Mod+Z", design.Undo)
                .On("Mod+Y", design.Redo)
                .On("Mod+D", design.DuplicateSelected)
                .On("Delete", design.DeleteSelected)
                .On("Backspace", design.DeleteSelected)
                .On("Escape", Escape);
        }

        protected override void OnParametersSet()
        {
            design.Update(Definition, Registry, History, Errors, Preview);
            if (Focus == focusSeen) return;
            focusSeen = Focus;
            if (Focus.Target != null && Definition?.FindDevice(Focus.Target) != null) design.SelectDevice(Focus.Target);
        }

        // The type card beside the palette stays away while something is dragged.
        private TypeTip VisibleTip => interaction.Dragging ? null : tip;

        private async Task BeginGesture(GestureStart start)
        {
            if (start.Gesture.Picks is { } picked) design.Select(picked);
            if (start.Pointer.Button != 0) return;
            interaction.Begin(start.Gesture, start.Pointer.ClientX, start.Pointer.ClientY, design.Definition.ToJson());
            var rect = await canvas.Measure();
            interaction.Place(start.Gesture, rect, start.Pointer.ClientX, start.Pointer.ClientY);
            await canvas.Focus();
        }

        private Task StartNewDevice((PointerEventArgs Pointer, string Type) start)
        {
            tip = null;
            return BeginGesture(new GestureStart(start.Pointer, new NewDeviceGesture(design, start.Type)));
        }

        private void OnPointerMove(PointerEventArgs e)
        {
            interaction.Move(e.ClientX, e.ClientY);
        }

        private Task OnPointerUp(PointerEventArgs e)
        {
            return interaction.Release(e.ClientX, e.ClientY, design.Layout);
        }

        private void Escape()
        {
            interaction.Drop();
            design.CancelDelete();
            design.ClearSelection();
        }

        private async Task Download()
        {
            var definition = design.Definition;
            var name = string.IsNullOrWhiteSpace(definition.Name) ? "machine" : definition.Name;
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{name}.machine.json", definition.ToJson());
        }
    }
}
