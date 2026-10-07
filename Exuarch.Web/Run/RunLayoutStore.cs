using System.Text.Json;
using System.Threading.Tasks;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    // Keeps the Run view's layout in this browser: where the panels are and how big, and which are folded away.
    public sealed class RunLayoutStore
    {
        private const string LayoutKey = "exuarch.runLayout";
        private const string PanelsKey = "exuarch.runPanels";

        private readonly IBrowserStore store;

        public RunLayoutStore(IBrowserStore store)
        {
            this.store = store;
        }

        // The layout kept, a new one when what was kept is not a layout, or null when nothing was kept.
        public async Task<SavedLayout> LoadLayout()
        {
            try
            {
                var json = await store.Get(LayoutKey);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonSerializer.Deserialize(json, RunLayoutJsonContext.Default.SavedLayout) ?? new SavedLayout();
            }
            catch (JsonException)
            {
                return new SavedLayout();
            }
        }

        public async Task SaveLayout(SavedLayout layout)
        {
            await store.Set(LayoutKey, JsonSerializer.Serialize(layout, RunLayoutJsonContext.Default.SavedLayout));
        }

        // The panels folded away, as RunLayout.CollapsedList writes them.
        public async Task<string> LoadCollapsed()
        {
            return await store.Get(PanelsKey);
        }

        public async Task SaveCollapsed(string collapsed)
        {
            await store.Set(PanelsKey, collapsed);
        }
    }
}
