using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Drawer
{
    public sealed record GalleryPreview(string Image, IReadOnlyList<string> Lcd);

    public sealed class GalleryPreviews
    {
        public const string Folder = "machines/";
        private readonly HttpClient http;
        private readonly NavigationManager navigation;
        private Dictionary<string, GalleryPreview> previews;
        private Task loading;

        public GalleryPreviews(HttpClient http, NavigationManager navigation)
        {
            this.http = http;
            this.navigation = navigation;
        }

        public GalleryPreview For(string name)
        {
            return previews != null && previews.TryGetValue(name, out var preview) ? preview : null;
        }

        public Task LoadAsync()
        {
            return loading ??= Load();
        }

        private async Task Load()
        {
            try
            {
                var json = await http.GetStringAsync(new Uri(new Uri(navigation.BaseUri), Folder + "previews.json"));
                previews = Parse(json, navigation.BaseUri + Folder);
            }
            catch (Exception e) when (e is HttpRequestException || e is JsonException || e is TaskCanceledException)
            {
                previews = new Dictionary<string, GalleryPreview>();
            }
        }

        public static Dictionary<string, GalleryPreview> Parse(string json, string imageBase)
        {
            var result = new Dictionary<string, GalleryPreview>();
            using var document = JsonDocument.Parse(json);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var image = entry.Value.TryGetProperty("image", out var file) ? imageBase + file.GetString() : null;
                var lcd = entry.Value.TryGetProperty("lcd", out var lines) ? lines.EnumerateArray().Select(l => l.GetString() ?? "").ToList() : null;
                result[entry.Name] = new GalleryPreview(image, lcd);
            }
            return result;
        }
    }
}
