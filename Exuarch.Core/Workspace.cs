using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    public class SavedPackage
    {
        public MachinePackage Package { get; set; }
        public string ProgramName { get; set; }
        public string ProgramSource { get; set; }
    }

    public class Workspace
    {
        public const int CurrentVersion = 1;
        public int Version { get; set; } = CurrentVersion;
        public string Open { get; set; }
        public List<SavedPackage> Packages { get; set; } = new List<SavedPackage>();

        public static bool IsBuiltIn(string name)
        {
            return BuiltInPackages.All.Any(p => p.Name == name);
        }

        public static Workspace FromJson(string json)
        {
            TryFromJson(json, out var workspace);
            return workspace;
        }

        public static bool TryFromJson(string json, out Workspace workspace)
        {
            workspace = new Workspace();
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                workspace = (JsonSerializer.Deserialize(json, MachineDefinitionJsonContext.Default.Workspace) ?? new Workspace()).Normalised();
                return true;
            }
            catch (Exception e) when (IsUnreadable(e))
            {
                workspace = new Workspace();
                return false;
            }
        }
        private static bool IsUnreadable(Exception e)
        {
            return e is JsonException or MachineDefinitionException or NotSupportedException or InvalidOperationException;
        }

        private Workspace Normalised()
        {
            Packages = (Packages ?? new List<SavedPackage>()).Where(IsWhole).GroupBy(p => p.Package.Name).Select(g => g.Last()).ToList();
            foreach (var saved in Packages) saved.Package.Machine.DropRemovedParameters();
            return this;
        }
        private static bool IsWhole(SavedPackage saved)
        {
            return saved?.Package?.Machine != null && !string.IsNullOrWhiteSpace(saved.Package.Name);
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, MachineDefinitionJsonContext.Default.Workspace);
        }

        public SavedPackage Find(string name)
        {
            return Packages.FirstOrDefault(p => p.Package.Name == name);
        }

        public bool IsEdited(string name)
        {
            var saved = Find(name);
            return IsBuiltIn(name) && saved != null && !IsAsShipped(saved, ignoreLayout: true);
        }

        public IEnumerable<MachinePackage> OwnPackages
        {
            get { return Packages.Where(p => !IsBuiltIn(p.Package.Name)).Select(p => p.Package); }
        }

        public (MachinePackage Package, string ProgramName, string ProgramSource)? Load(string name)
        {
            var saved = Find(name);
            if (saved != null) return (saved.Package.Clone(), saved.ProgramName, saved.ProgramSource);
            if (IsBuiltIn(name)) return (BuiltInPackages.Get(name), null, null);
            return null;
        }

        public void Save(MachinePackage package, string programName, string programSource)
        {
            Open = package.Name;
            var saved = new SavedPackage { Package = package.Clone(), ProgramName = programName, ProgramSource = programSource };
            if (IsBuiltIn(package.Name) && IsAsShipped(saved))
            {
                Remove(package.Name);
                return;
            }
            int index = Packages.FindIndex(p => p.Package.Name == package.Name);
            if (index >= 0) Packages[index] = saved;
            else Packages.Add(saved);
        }

        public void Remove(string name)
        {
            Packages.RemoveAll(p => p.Package.Name == name);
        }

        private static bool IsAsShipped(SavedPackage saved, bool ignoreLayout = false)
        {
            var shipped = BuiltInPackages.Get(saved.Package.Name);
            var mine = saved.Package.Clone();
            foreach (var machine in new[] { shipped.Machine, mine.Machine })
            {
                if (ignoreLayout) WithoutLayout(machine);
                else machine.EnsureLayout();
            }
            if (mine.ToJson() != shipped.ToJson()) return false;
            var first = shipped.Programs.FirstOrDefault();
            if (saved.ProgramSource == null) return true;
            var open = shipped.Programs.FirstOrDefault(p => p.Name == saved.ProgramName);
            return open != null ? open.Source == saved.ProgramSource : first != null && saved.ProgramSource == first.Source;
        }

        private static void WithoutLayout(MachineDefinition machine)
        {
            foreach (var bus in machine.Buses) bus.Layout = null;
            foreach (var device in machine.Devices) device.Layout = null;
            if (machine.Decoder != null) machine.Decoder.Layout = null;
        }

        public string UniqueName(string name)
        {
            var unique = name;
            for (int n = 2; IsBuiltIn(unique) || Find(unique) != null; n++) unique = $"{name} {n}";
            return unique;
        }
    }
}
