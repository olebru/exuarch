using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Exuarch.Web.Run
{
    public partial class RunView
    {
        private readonly RunLayout runLayout = new RunLayout();
        private RunLayoutStore store;
        private PanelActions actions;
        private bool layoutLoaded;
        private DotNetObjectReference<RunView> selfReference;

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

        private async Task TogglePanel(string key)
        {
            runLayout.Toggle(key);
            ScrollListingAgain();
            await store.SaveCollapsed(runLayout.CollapsedList);
        }

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

        [JSInvokable]
        public async Task LayoutResized(string kind, double size)
        {
            runLayout.Saved.Resize(kind, size);
            await SaveLayout();
        }
    }
}
