using System;
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
    public readonly record struct GestureStart(PointerEventArgs Pointer, CanvasGesture Gesture);

    public partial class DesignCanvas : IDisposable
    {
        [Inject] private IJSRuntime JS { get; set; }

        [Parameter] public MachineDesign Design { get; set; }
        [Parameter] public CanvasInteraction Interaction { get; set; }
        [Parameter] public EventCallback<GestureStart> OnGesture { get; set; }
        [Parameter] public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }
        [Parameter] public bool ShowsControlWires { get; set; }
        [Parameter] public EventCallback OnZoomed { get; set; }

        private ElementReference scroller;
        private DotNetObjectReference<DesignCanvas> selfReference;
        private ElementReference surface;

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

        private List<ControlWire> ControlWires(SchematicLayout layout)
        {
            var targets = ControlWiring.TargetsOf(Design.Definition, Design.Info, id => Design.Preview?.Device(id));
            return new ControlWiring(Design.Definition, layout).Route(targets);
        }

        private const string Grabbable = ".dev, .bus, button, input, select, textarea, a, label";

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || scroller.Context == null) return;
            await JS.InvokeVoidAsync("exuarchKeys.editor", scroller);
            await JS.InvokeVoidAsync("exuarchPan.attach", scroller, Grabbable);
            selfReference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("exuarchWheelZoom.attach", scroller, selfReference);
        }

        [JSInvokable]
        public async Task<ZoomChange> WheelZoom(int steps)
        {
            var change = Interaction.ZoomByWheelSteps(steps);
            await OnZoomed.InvokeAsync();
            return change;
        }

        public void Dispose() => selfReference?.Dispose();

        public ValueTask<ElementRect> Measure() => JS.InvokeAsync<ElementRect>("exuarchEditor.rect", surface);

        public ValueTask Focus() => JS.InvokeVoidAsync("exuarchEditor.focus", scroller);
    }
}
