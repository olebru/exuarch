using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace Exuarch.Core
{
    // One package as it was left: the package with the machine as edited, and the program that was open.
    public class SavedPackage
    {
        public MachinePackage Package { get; set; }
        // The name of the package program that was open, and the text in the editor, which differs from that
        // program when a built in example was edited without saving it as a program of its own.
        public string ProgramName { get; set; }
        public string ProgramSource { get; set; }
    }

    // What the app keeps in the browser between visits: every package that differs from the way it ships, and
    // which one was open. A built in package is only kept while it has changes, so Reset simply forgets it; the
    // user's own packages are always kept until they are deleted.
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

        // A workspace from what the browser kept; anything unreadable gives an empty one rather than an error,
        // so a damaged save never stops the app from starting.
        public static Workspace FromJson(string json)
        {
            TryFromJson(json, out var workspace);
            return workspace;
        }

        // False when there was something but it could not be read, so the caller can keep it aside instead of
        // overwriting it. Nothing at all is not a failure.
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

        // Keeps the last of the packages saved under each name, leaves out any that is not a whole package, and opens
        // each machine as a file would (MachineDefinition.FromJson).
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

        // A built in package the user has changed. Where its parts are drawn is kept, but moving them around is not a
        // change to the machine, so it does not count.
        public bool IsEdited(string name)
        {
            var saved = Find(name);
            return IsBuiltIn(name) && saved != null && !IsAsShipped(saved, ignoreLayout: true);
        }

        // The user's own packages, in the order they were first saved.
        public IEnumerable<MachinePackage> OwnPackages
        {
            get { return Packages.Where(p => !IsBuiltIn(p.Package.Name)).Select(p => p.Package); }
        }

        // The package to open under this name: the saved one if there is one, otherwise the built in one as it
        // ships. A copy either way, so editing it does not change the workspace until it is saved again.
        public (MachinePackage Package, string ProgramName, string ProgramSource)? Load(string name)
        {
            var saved = Find(name);
            if (saved != null) return (saved.Package.Clone(), saved.ProgramName, saved.ProgramSource);
            if (IsBuiltIn(name)) return (BuiltInPackages.Get(name), null, null);
            return null;
        }

        // Keeps the package as it is now. A built in package that is back the way it ships (with the editor's
        // layout, and its first program open unchanged) is forgotten instead, so it no longer shows as edited.
        public void Save(MachinePackage package, string programName, string programSource)
        {
            Open = package.Name;
            var saved = new SavedPackage { Package = package.Clone(), ProgramName = programName, ProgramSource = programSource };
            if (IsBuiltIn(package.Name) && IsAsShipped(saved))
            {
                Forget(package.Name);
                return;
            }
            int index = Packages.FindIndex(p => p.Package.Name == package.Name);
            if (index >= 0) Packages[index] = saved;
            else Packages.Add(saved);
        }

        public void Forget(string name)
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

        // A name no package, built in or saved, uses yet: the name itself, or with a number after it.
        public string UniqueName(string name)
        {
            var unique = name;
            for (int n = 2; IsBuiltIn(unique) || Find(unique) != null; n++) unique = $"{name} {n}";
            return unique;
        }
    }
}
