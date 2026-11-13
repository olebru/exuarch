using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Exuarch.Web.Components;
using System;
using System.Threading.Tasks;

namespace Exuarch.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddScoped<HelpService>();
            builder.Services.AddScoped<Analytics>();
            builder.Services.AddSingleton(new System.Net.Http.HttpClient());
            builder.Services.AddSingleton<Exuarch.Web.Drawer.CommunityCatalog>();
            builder.Services.AddScoped<Exuarch.Web.Drawer.GalleryPreviews>();
            builder.Services.AddSingleton(Exuarch.Core.DeviceRegistry.CreateDefault());
            builder.Services.AddSingleton<UiLevel>();

            var host = builder.Build();
            var js = host.Services.GetRequiredService<IJSRuntime>();
            KeyChord.OnMac = await js.InvokeAsync<bool>("exuarchKeys.isMac");
            host.Services.GetRequiredService<UiLevel>().Load(await StoredUiLevel(js));
            await host.RunAsync();
        }

        private static async Task<string> StoredUiLevel(IJSRuntime js)
        {
            try
            {
                return await js.InvokeAsync<string>("exuarchUi.current");
            }
            catch (JSException)
            {
                return UiLevel.EverythingName;
            }
        }
    }
}
