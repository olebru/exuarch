using System.Text.Json;
using System.Threading.Tasks;
using Exuarch.Web.Components;

namespace Exuarch.Web.Run
{
    public sealed class RunLayoutStore
    {
        private const string LayoutKey = "exuarch.runLayout";
        private const string PanelsKey = "exuarch.runPanels";

        private readonly IBrowserStore store;

        public RunLayoutStore(IBrowserStore store)
        {
            this.store = store;
        }

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
