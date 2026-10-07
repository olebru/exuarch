using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    // A machine with its microcode and example programs, loaded and saved together. As a file it is one JSON
    // document; the built in packages are folders (package.json, machine.json and .asm files) embedded in the
    // assembly.
    public class MachinePackage
    {
        public string Name { get; set; }
        // One or two sentences, for lists and the package picker.
        public string Description { get; set; }
        // A longer introduction in Markdown, like a README: the ideas behind the machine, its parts, how its
        // instructions work and what to try. Links written exuarch:<kind>/<target> jump to that part of the app; the
        // kinds are listed in ReadmeLinks.
        public string Readme { get; set; }
        public MachineDefinition Machine { get; set; }
        public List<PackageProgram> Programs { get; set; } = new List<PackageProgram>();

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
        // What the program has to have from the Run view to work at all, or null for nothing special.
        public ProgramNeeds Needs { get; set; }
    }

    // What a program needs from whoever runs it, so the Run view can say what is missing and offer to fix it.
    public class ProgramNeeds
    {
        // The program reads the keypad, so the keyboard has to be captured on it.
        public bool? Keypad { get; set; }
        // The slowest clock speed, in Hz, at which the program works.
        public int? MinHz { get; set; }
        // The one clock speed, in Hz, at which the program works, such as a program timed by counting ticks.
        public int? ExactHz { get; set; }

        public bool IsEmpty() { return Keypad != true && (MinHz is null or <= 0) && (ExactHz is null or <= 0); }
    }

    // The package.json of a built in package folder.
    internal class PackageManifest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        // The README file in the package folder, if any.
        public string Readme { get; set; }
        // The package the app opens with. Exactly one built in package sets it.
        public bool Default { get; set; }
        // simple, advanced or ludicrous: how much of the app a machine expects you to know.
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
        private static readonly Lazy<IReadOnlyList<MachinePackage>> all = new Lazy<IReadOnlyList<MachinePackage>>(Load);

        // How far into the app each example goes, for grouping them in the picker.
        public static readonly string[] Levels = { "simple", "advanced", "ludicrous" };
        private static readonly Dictionary<string, string> levels = new Dictionary<string, string>();

        // The default first, then the others in folder name order.
        public static IReadOnlyList<MachinePackage> All { get { return all.Value; } }

        // The level of the built in package with this name, one of Levels, or null for any other name.
        public static string Level(string name)
        {
            _ = all.Value;
            return name != null && levels.TryGetValue(name, out var level) ? level : null;
        }
        // The package whose package.json says "default": true.
        public static MachinePackage Default { get { return All[0]; } }

        // A fresh copy that can be changed without affecting the built in one.
        public static MachinePackage Get(string name)
        {
            return (All.FirstOrDefault(p => p.Name == name) ?? throw new ArgumentException($"No built in package '{name}'.")).Clone();
        }

        private static IReadOnlyList<MachinePackage> Load()
        {
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
                if (!Levels.Contains(manifest.Level))
                    throw new InvalidOperationException($"Built in package '{manifest.Name}' needs a \"level\": {string.Join(", ", Levels)}.");
                levels[manifest.Name] = manifest.Level;
                return (manifest.Default, Package: new MachinePackage
                {
                    Name = manifest.Name,
                    Description = manifest.Description,
                    Readme = manifest.Readme == null ? null : Read(manifest.Readme).TrimEnd('\n', '\r'),
                    Machine = MachineDefinition.FromJson(Read(manifest.Machine)),
                    Programs = manifest.Programs.Select(p => new PackageProgram { Name = p.Name, Description = p.Description, Source = Read(p.File).TrimEnd('\n', '\r'), Needs = p.Needs }).ToList(),
                });
            }).ToList();
            var defaults = packages.Where(p => p.Default).Select(p => p.Package.Name).ToList();
            if (defaults.Count != 1)
                throw new InvalidOperationException($"Exactly one built in package must be the default, found {defaults.Count}: {string.Join(", ", defaults)}.");
            return packages.OrderBy(p => p.Default ? 0 : 1).Select(p => p.Package).ToList();
        }
    }
}
