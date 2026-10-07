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
            // The device types, with real time clocks that read the browser's clock.
            builder.Services.AddSingleton(Exuarch.Core.DeviceRegistry.CreateDefault());

            var host = builder.Build();
            KeyChord.OnMac = await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<bool>("exuarchKeys.isMac");
            await host.RunAsync();
        }
    }
}
