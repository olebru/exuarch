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
    // The machine running: its controls, the live drawing, and the panels around it (the rest of this folder). The
    // session holds the machine and what the panels share, and this view puts it on the page: it lays the panels out,
    // takes the keyboard, and draws itself again whenever the session changes.
    public partial class RunView : IDisposable, IRunHost
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private Analytics Analytics { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }

        [Parameter] public Machine Machine { get; set; }
        [Parameter] public EventCallback OnRestart { get; set; }
        // Shown in the top bar: which machine this is and which program is loaded.
        [Parameter] public string MachineName { get; set; }
        [Parameter] public string ProgramName { get; set; }
        // What the program needs to work; the view warns about each need that is not met and offers to meet it.
        [Parameter] public ProgramNeeds ProgramNeeds { get; set; }
        // Start running as soon as the view is shown: "slow" at the slider's speed, "max" as fast as it goes. The page
        // clears it in AutoStarted, so it happens once.
        [Parameter] public string AutoStart { get; set; }
        [Parameter] public EventCallback AutoStarted { get; set; }

        private RunSession session;
        // The machine's panels, made again for every machine.
        private List<RunPanel> panels;
        private KeyMap shortcuts;
        private int? lastScrolledAddress;
        private bool fitPending = true;
        private readonly SchematicZoom zoom = new SchematicZoom();
        private ElementReference schematicElement;
        private ElementReference runElement;
        // The Run view element the keyboard and resize handlers are attached to.
        private string attachedElement;

        protected override void OnInitialized()
        {
            session = new RunSession(Analytics, Registry, () => JS.InvokeVoidAsync("exuarchEditor.focus", runElement).AsTask());
            session.Changed += StateHasChanged;
            store = new RunLayoutStore(new BrowserStore(JS));
            // A letter matches in either case, and a chord without Shift matches with it too, so Shift+→ comes first.
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
            lastScrolledAddress = null;
            fitPending = true;
            panels = Machine == null ? null : RunPanels.For(session);
        }

        // After a render, whatever is due: the layout this browser kept, once; the keyboard and resize handlers, once per
        // Run view element (it is made again when the machine goes away and comes back); fitting the drawing; scrolling
        // the listing to the current instruction; and a run the page asked for.
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await LoadLayoutOnce();
            if (Machine == null) return;
            await AttachOnce();
            await FitIfPending();
            await ScrollToCurrent();
            await StartIfAsked();
        }

        private async Task AttachOnce()
        {
            if (runElement.Context == null || attachedElement == runElement.Id) return;
            attachedElement = runElement.Id;
            await JS.InvokeVoidAsync("exuarchKeys.runView", runElement);
            selfReference ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("exuarchRunLayout.attach", runElement, selfReference);
            await JS.InvokeVoidAsync("exuarchRunLayout.observe", schematicElement, selfReference);
        }

        private async Task FitIfPending()
        {
            if (!fitPending) return;
            fitPending = false;
            await Fit();
        }

        private async Task ScrollToCurrent()
        {
            var address = Machine.CurrentInstructionAddress;
            if (address == null || address == lastScrolledAddress) return;
            lastScrolledAddress = address;
            await JS.InvokeVoidAsync("exuarchEditor.scrollToId", $"listing-{address}");
        }

        // A run the page asked for, such as the splash screen's: started once, when the view is on the screen.
        private async Task StartIfAsked()
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

        // ---- Zoom ----

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

        // ---- Running ----

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

        // ---- Keyboard ----

        // A keypad that has the keyboard takes its keys first; the rest are shortcuts.
        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (session.Keys.KeyDown(e.Key)) return;
            await shortcuts.Dispatch(e);
        }

        private void OnKeyUp(KeyboardEventArgs e)
        {
            session.Keys.KeyUp(e.Key);
        }

        // ---- The drawing's geometry, the same as the hardware design canvas uses ----

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
