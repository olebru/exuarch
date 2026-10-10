using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    public class CommunityMachine
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Tagline { get; set; }
        public string Description { get; set; }
        public List<string> Tags { get; set; }
        public string Level { get; set; }
        public string MinVersion { get; set; }
        public string File { get; set; }
        public string Image { get; set; }

        public string Summary => string.IsNullOrWhiteSpace(Tagline) ? new MachinePackage { Description = Description }.Summary : Tagline.Trim();

        public bool Matches(string query)
        {
            return new MachinePackage { Name = Name, Tagline = Tagline, Description = $"{Description} {Author}", Tags = Tags }.Matches(query);
        }

        public bool RunsOn(string appVersion)
        {
            if (!CommunityIndex.TryVersion(MinVersion, out var needed)) return true;
            if (!CommunityIndex.TryVersion(appVersion, out var app)) return true;
            return app.CompareTo(needed) >= 0;
        }
    }

    public class CommunityIndex
    {
        public const int MaxIndexLength = 512 * 1024;
        public const int MaxPackageLength = 4 * 1024 * 1024;

        public List<CommunityMachine> Machines { get; set; } = new List<CommunityMachine>();

        public static CommunityIndex FromJson(string json)
        {
            if (json == null || json.Length > MaxIndexLength) throw new MachineDefinitionException("The community index is missing or too large.");
            CommunityIndex index;
            try
            {
                index = JsonSerializer.Deserialize(json, CommunityJsonContext.Default.CommunityIndex);
            }
            catch (JsonException e)
            {
                throw new MachineDefinitionException($"The community index is not valid JSON: {e.Message}");
            }
            index ??= new CommunityIndex();
            index.Machines = (index.Machines ?? new List<CommunityMachine>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name) && IsSafeRelativePath(m.File))
                .Select(m => { if (!IsSafeRelativePath(m.Image)) m.Image = null; return m; })
                .GroupBy(m => m.Name.Trim(), StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return index;
        }

        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 200) return false;
            if (path.StartsWith("/") || path.Contains('\\') || path.Contains(':') || path.Contains('?') || path.Contains('#')) return false;
            return path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }

        public static Uri Resolve(Uri indexUrl, string relativePath)
        {
            if (!IsSafeRelativePath(relativePath)) throw new ArgumentException($"'{relativePath}' is not a path inside the community collection.");
            return new Uri(indexUrl, string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString)));
        }

        public static bool TryVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var core = text.Trim().TrimStart('v');
            int dash = core.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) core = core.Substring(0, dash);
            return Version.TryParse(core, out version);
        }
    }

    [System.Text.Json.Serialization.JsonSourceGenerationOptions(
        PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(CommunityIndex))]
    internal partial class CommunityJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
