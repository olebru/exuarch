using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Exuarch.Web.Run
{
    // The viewer's changes to the Run view's layout (see RunLayout), carried out and kept in this browser.
    public partial class RunView
    {
        private readonly RunLayout runLayout = new RunLayout();
        private RunLayoutStore store;
        private PanelActions actions;
        private bool layoutLoaded;
        private DotNetObjectReference<RunView> selfReference;

        // The layout this browser kept, and what it kept folded away. Folded panels change the drawing's width, so it is
        // fitted again.
        private async Task LoadLayoutOnce()
        {
            if (layoutLoaded) return;
            layoutLoaded = true;
            runLayout.Load(await store.LoadLayout());
            if (!runLayout.FoldSaved(await store.LoadCollapsed())) return;
            fitPending = true;
            StateHasChanged();
        }

        private Task SaveLayout() => store.SaveLayout(runLayout.Saved);

        // Folds a panel, a column, the control lines or the panels below away, or opens it.
        private async Task TogglePanel(string key)
        {
            runLayout.Toggle(key);
            // The listing needs scrolling to the current line again; a change of width is left to SchematicResized.
            lastScrolledAddress = null;
            await store.SaveCollapsed(runLayout.CollapsedList);
        }

        // A tab below shows its panel, and opens the panels below if they were folded away.
        private async Task ShowBottom(Place tab)
        {
            runLayout.ShowTab(tab.Dock, tab.PanelId);
            await Unfold(Dock.Bottom.Key());
        }

        private async Task Unfold(string key)
        {
            if (runLayout.Collapsed(key)) await TogglePanel(key);
        }

        private async Task ToggleBottomSplit()
        {
            runLayout.ToggleSplit(runLayout.Place(panels));
            await Unfold(Dock.Bottom.Key());
            await SaveLayout();
        }

        // A dock a panel is dropped into opens if it was folded away.
        private async Task DropPanel(Place place)
        {
            if (!runLayout.Drop(place, runLayout.Place(panels))) return;
            await Unfold(place.Dock.Fold());
            await SaveLayout();
        }

        private async Task ResetLayout()
        {
            foreach (var key in runLayout.Reset()) await TogglePanel(key);
            await SaveLayout();
        }

        // The resize handles are dragged in the browser, and only the size they end on comes back here.
        [JSInvokable]
        public async Task LayoutResized(string kind, double size)
        {
            runLayout.Saved.Resize(kind, size);
            // The drawing's panel changed width, and SchematicResized fits it again unless the viewer has zoomed.
            await SaveLayout();
        }
    }
}
