using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    public class MachinePackage
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Tagline { get; set; }
        public List<string> Tags { get; set; }
        public string Preview { get; set; }
        public string Readme { get; set; }
        public MachineDefinition Machine { get; set; }
        public List<PackageProgram> Programs { get; set; } = new List<PackageProgram>();

        [System.Text.Json.Serialization.JsonIgnore]
        public string Summary
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Tagline)) return Tagline.Trim();
                var text = (Description ?? "").Trim();
                int end = text.IndexOfAny(new[] { '.', ':', ';' });
                return end > 0 ? text.Substring(0, end) : text;
            }
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var haystack = string.Join(" ", new[] { Name, Tagline, Description }.Concat(Tags ?? new List<string>()));
            return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        public PackageProgram Program(string name)
        {
            return Programs.FirstOrDefault(p => p.Name == name) ?? throw new ArgumentException($"Package '{Name}' has no program '{name}'.");
        }

        public string ToJson()
        {
            return CompactJson.Format(JsonSerializer.Serialize(this, MachineDefinitionJsonContext.Default.MachinePackage));
        }
        public static MachinePackage FromJson(string json)
        {
            try
            {
                var package = JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.MachinePackage)
                              ?? throw new MachineDefinitionException("Package is empty.");
                if (package.Machine == null) throw new MachineDefinitionException("A package needs a \"machine\".");
                package.Machine.DropRemovedParameters();
                return package;
            }
            catch (JsonException e)
            {
                var location = e.Path == null ? "" : $" at {e.Path} (line {e.LineNumber + 1})";
                throw new MachineDefinitionException($"Package is not valid JSON{location}: {e.Message}");
            }
        }
        public MachinePackage Clone()
        {
            return FromJson(ToJson());
        }
    }

    public class PackageProgram
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public ProgramNeeds Needs { get; set; }
    }

    public class ProgramNeeds
    {
        public bool? Keypad { get; set; }
        public int? MinHz { get; set; }
        public int? ExactHz { get; set; }

        public bool IsEmpty() { return Keypad != true && (MinHz is null or <= 0) && (ExactHz is null or <= 0); }
    }

    internal class PackageManifest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Tagline { get; set; }
        public List<string> Tags { get; set; }
        public string Preview { get; set; }
        public string Readme { get; set; }
        public bool Default { get; set; }
        public string Level { get; set; }
        public string Machine { get; set; }
        public List<ManifestProgram> Programs { get; set; } = new List<ManifestProgram>();
    }
    internal class ManifestProgram
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string File { get; set; }
        public ProgramNeeds Needs { get; set; }
    }

    public static class BuiltInPackages
    {
        private const string Prefix = "Exuarch.Core.Packages/";
        private static readonly Lazy<Catalog> catalog = new Lazy<Catalog>(Load);

        private sealed record Catalog(IReadOnlyList<MachinePackage> All, Dictionary<string, string> Levels, Dictionary<string, string> ShippedJson);

        public const string TutorialLevel = "tutorial";
        public static readonly string[] Levels = { "simple", "advanced", "ludicrous", TutorialLevel };

        public static IReadOnlyList<MachinePackage> All { get { return catalog.Value.All; } }

        public static IEnumerable<MachinePackage> Examples { get { return All.Where(p => Level(p.Name) != TutorialLevel); } }

        public static string Level(string name)
        {
            return name != null && catalog.Value.Levels.TryGetValue(name, out var level) ? level : null;
        }
        public static MachinePackage Default { get { return All[0]; } }

        public static MachinePackage Get(string name)
        {
            return catalog.Value.ShippedJson.TryGetValue(name ?? "", out var json) ? MachinePackage.FromJson(json) : throw new ArgumentException($"No built in package '{name}'.");
        }

        private static Catalog Load()
        {
            var levels = new Dictionary<string, string>();
            var assembly = typeof(BuiltInPackages).Assembly;
            var folders = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix) && n.EndsWith("/package.json"))
                .Select(n => n.Substring(0, n.Length - "package.json".Length))
                .OrderBy(n => n, StringComparer.Ordinal);
            var packages = folders.Select(folder =>
            {
                string Read(string file)
                {
                    using var stream = assembly.GetManifestResourceStream(folder + file)
                        ?? throw new InvalidOperationException($"Package file '{folder + file}' is missing.");
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
                var manifest = JsonSerializer.Deserialize(Read("package.json"), MachineDefinitionJsonContext.Default.PackageManifest);
                if (!Levels.Contains(manifest.Level) || manifest.Level == TutorialLevel)
                    throw new InvalidOperationException($"Built in package '{manifest.Name}' needs a \"level\": {string.Join(", ", Levels)}.");
                levels[manifest.Name] = manifest.Level;
                return (manifest.Default, Package: new MachinePackage
                {
                    Name = manifest.Name,
                    Description = manifest.Description,
                    Tagline = manifest.Tagline,
                    Tags = manifest.Tags,
                    Preview = manifest.Preview,
                    Readme = manifest.Readme == null ? null : Read(manifest.Readme).TrimEnd('\n', '\r'),
                    Machine = MachineDefinition.FromJson(Read(manifest.Machine)),
                    Programs = manifest.Programs.Select(p => new PackageProgram { Name = p.Name, Description = p.Description, Source = Read(p.File).TrimEnd('\n', '\r'), Needs = p.Needs }).ToList(),
                });
            }).ToList();
            var defaults = packages.Where(p => p.Default).Select(p => p.Package.Name).ToList();
            if (defaults.Count != 1)
                throw new InvalidOperationException($"Exactly one built in package must be the default, found {defaults.Count}: {string.Join(", ", defaults)}.");
            var all = packages.OrderBy(p => p.Default ? 0 : 1).Select(p => p.Package).ToList();
            foreach (var start in TutorialMachines.Starts)
            {
                var package = TutorialMachines.Package(start);
                levels[package.Name] = TutorialLevel;
                all.Add(package);
            }
            var shipped = new Dictionary<string, string>();
            foreach (var package in all) shipped[package.Name] = package.ToJson();
            return new Catalog(all, levels, shipped);
        }
    }
}
