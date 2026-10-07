using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Exuarch.Web.Components
{
    // What is kept between visits, by key: the workspace, and how the Run view is laid out.
    public interface IBrowserStore
    {
        Task<string> Get(string key);
        // Whether it could be kept: storage can be off or full.
        Task<bool> Set(string key, string value);
        // Keeps a value without asking whether that worked.
        Task Keep(string key, string value);
        // Forgets everything kept, and loads the page afresh.
        Task ResetAll();
    }

    // The browser's local storage, through exuarchStore in machineEditor.js.
    public sealed class BrowserStore : IBrowserStore
    {
        private readonly IJSRuntime js;

        public BrowserStore(IJSRuntime js)
        {
            this.js = js;
        }

        public Task<string> Get(string key) => js.InvokeAsync<string>("exuarchStore.get", key).AsTask();
        public Task<bool> Set(string key, string value) => js.InvokeAsync<bool>("exuarchStore.set", key, value).AsTask();
        public Task Keep(string key, string value) => js.InvokeVoidAsync("exuarchStore.set", key, value).AsTask();
        public Task ResetAll() => js.InvokeVoidAsync("exuarchStore.resetAll").AsTask();
    }
}
