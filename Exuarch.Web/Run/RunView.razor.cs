using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Devices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.Run
{
    public partial class RunView : IDisposable, IRunHost
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private Analytics Analytics { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }

        [Parameter] public Machine Machine { get; set; }
        [Parameter] public EventCallback OnRestart { get; set; }
        [Parameter] public string MachineName { get; set; }
        [Parameter] public string ProgramName { get; set; }
        [Parameter] public ProgramNeeds ProgramNeeds { get; set; }
        [Parameter] public string AutoStart { get; set; }
        [Parameter] public EventCallback AutoStarted { get; set; }

        private RunSession session;
        private List<RunPanel> panels;
        private KeyMap shortcuts;
        private int? lastScrolledAddress;
        private bool fitPending = true;
        private readonly SchematicZoom zoom = new SchematicZoom();
        private ElementReference schematicElement;
        private ElementReference runElement;
        private string attachedElement;

        protected override void OnInitialized()
        {
            session = new RunSession(Analytics, Registry, () => JS.InvokeVoidAsync("exuarchEditor.focus", runElement).AsTask());
            session.Changed += StateHasChanged;
            store = new RunLayoutStore(new BrowserStore(JS));
            shortcuts = new KeyMap()
                .On(" ", ToggleRun)
                .On("m", ToggleMax)
                .On("Shift+ArrowRight", session.StepInstruction)
                .On("ArrowRight", session.TickOnce)
                .On("r", Restart);
            actions = new PanelActions(
                EventCallback.Factory.Create<string>(this, TogglePanel),
                EventCallback.Factory.Create<string>(this, runLayout.StartDrag),
                EventCallback.Factory.Create(this, runLayout.EndDrag),
                EventCallback.Factory.Create<Place>(this, runLayout.DragOver),
                EventCallback.Factory.Create<Place>(this, DropPanel),
                EventCallback.Factory.Create<Place>(this, ShowBottom),
                EventCallback.Factory.Create(this, ToggleBottomSplit));
        }

        protected override void OnParametersSet()
        {
            if (!session.Show(Machine)) return;
            ScrollListingAgain();
            fitPending = true;
            panels = Machine == null ? null : RunPanels.For(session);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await LoadLayoutOnce();
            if (Machine == null) return;
            await AttachOnce();
            await FitIfPending();
            await ScrollToCurrent();
            await AutoStartIfSet();
        }

        private async Task AttachOnce()
        {
            if (runElement.Context == null || attachedElement == runElement.Id) return;
            attachedElement = runElement.Id;
            await JS.InvokeVoidAsync("exuarchKeys.runView", runElement);
            selfReference ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("exuarchRunLayout.attach", runElement, selfReference);
            await JS.InvokeVoidAsync("exuarchRunLayout.observe", schematicElement, selfReference);
            await JS.InvokeVoidAsync("exuarchPan.attach", schematicElement, SchematicGrabbable);
            await JS.InvokeVoidAsync("exuarchWheelZoom.attach", schematicElement, selfReference);
        }

        [JSInvokable]
        public ZoomChange WheelZoom(int steps)
        {
            var change = zoom.ByWheelSteps(steps);
            StateHasChanged();
            return change;
        }

        private const string SchematicGrabbable = ".dev, .bus-toggle, .zoom, button, input, select, textarea, a, label";

        private async Task FitIfPending()
        {
            if (!fitPending) return;
            fitPending = false;
            await Fit();
        }

        private void ScrollListingAgain() => lastScrolledAddress = null;

        private async Task ScrollToCurrent()
        {
            var address = Machine.CurrentInstructionAddress;
            if (address == null || address == lastScrolledAddress) return;
            lastScrolledAddress = address;
            await JS.InvokeVoidAsync("exuarchEditor.scrollToId", $"listing-{address}");
        }

        private async Task AutoStartIfSet()
        {
            if (AutoStart == null || session.Running) return;
            session.Speed.Max = AutoStart == "max";
            await AutoStarted.InvokeAsync();
            _ = ToggleRun();
        }

        public void Dispose()
        {
            session.Changed -= StateHasChanged;
            session.Stop();
            selfReference?.Dispose();
        }

        private async Task Fit()
        {
            zoom.StartFitting();
            var rect = await JS.InvokeAsync<ElementRect>("exuarchEditor.rect", schematicElement);
            if (zoom.Fit(rect.Width, Layout.ContentWidth)) StateHasChanged();
        }

        [JSInvokable]
        public void SchematicResized(double width)
        {
            if (zoom.Resized(width, Layout.ContentWidth)) StateHasChanged();
        }

        private Task ToggleRun() => session.ToggleRun(this);

        private void ToggleMax() => session.Speed.Max = !session.Speed.Max;

        private async Task Restart()
        {
            session.Stop();
            await OnRestart.InvokeAsync();
        }

        void IRunHost.Redraw() => StateHasChanged();
        void IRunHost.ShowScreens() => FramebufferView.ShowChanges();
        Task IRunHost.NextTurn() => ScreenInterop.NextTurn();
        Task IRunHost.Delay(int milliseconds) => Task.Delay(milliseconds);

        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (session.Keys.KeyDown(e.Key)) return;
            await shortcuts.Dispatch(e);
        }

        private void OnKeyUp(KeyboardEventArgs e)
        {
            session.Keys.KeyUp(e.Key);
        }

        private SchematicLayout Layout
        {
            get
            {
                if (!ReferenceEquals(layoutFor, Machine.Definition)) { layoutFor = Machine.Definition; layout = new SchematicLayout(Machine.Definition, Registry); }
                return layout;
            }
        }
        private SchematicLayout layout;
        private MachineDefinition layoutFor;
    }
}
