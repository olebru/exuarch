using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.Hardware
{
    // A pointer going down on the canvas or the palette, and the gesture it starts.
    public readonly record struct GestureStart(PointerEventArgs Pointer, CanvasGesture Gesture);

    // The schematic: buses, wires and cards, drawn to scale, and what a gesture in progress draws over them. Starting a
    // gesture and the keys pressed on the canvas go to the editor.
    public partial class DesignCanvas
    {
        [Inject] private IJSRuntime JS { get; set; }

        [Parameter] public MachineDesign Design { get; set; }
        [Parameter] public CanvasInteraction Interaction { get; set; }
        [Parameter] public EventCallback<GestureStart> OnGesture { get; set; }
        [Parameter] public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }

        private ElementReference scroller;
        private ElementReference surface;

        // The drawing's geometry. While something is being moved, the wires are quick curves; they are routed again
        // once it is dropped.
        private SchematicLayout Layout
        {
            get
            {
                var layout = Design.Layout;
                layout.Draft = Interaction.IsLayoutDraft;
                return layout;
            }
        }

        private IEnumerable<DeviceDefinition> PlacedDevices => Design.Definition.Devices.Where(d => d.Layout != null);

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && scroller.Context != null) await JS.InvokeVoidAsync("exuarchKeys.editor", scroller);
        }

        // Where the canvas is in the browser window, to turn pointer positions into canvas coordinates.
        public ValueTask<ElementRect> Measure() => JS.InvokeAsync<ElementRect>("exuarchEditor.rect", surface);

        // Keys go to the canvas while something on it is dragged.
        public ValueTask Focus() => JS.InvokeVoidAsync("exuarchEditor.focus", scroller);
    }
}
