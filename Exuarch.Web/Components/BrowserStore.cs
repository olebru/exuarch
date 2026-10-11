using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Exuarch.Web.Components
{
    public interface IBrowserStore
    {
        Task<string> Get(string key);
        Task<bool> Set(string key, string value);
        Task Keep(string key, string value);
        Task ResetAll();
    }

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
