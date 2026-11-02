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
        public List<SavedPackage> Drafts { get; set; } = new List<SavedPackage>();

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
            Packages = Whole(Packages);
            Drafts = Whole(Drafts).Where(d => Packages.Any(p => p.Package.Name == d.Package.Name) || IsBuiltIn(d.Package.Name)).ToList();
            foreach (var saved in Packages.Concat(Drafts)) saved.Package.CheckFileFormat();
            return this;
        }
        private static List<SavedPackage> Whole(List<SavedPackage> packages)
        {
            return (packages ?? new List<SavedPackage>()).Where(IsWhole).GroupBy(p => p.Package.Name).Select(g => g.Last()).ToList();
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

        public SavedPackage FindDraft(string name)
        {
            return Drafts.FirstOrDefault(p => p.Package.Name == name);
        }

        public bool HasUnsavedChanges(string name) => FindDraft(name) != null;

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
            var draft = FindDraft(name);
            if (draft != null) return (draft.Package.Clone(), draft.ProgramName, draft.ProgramSource);
            return LoadSaved(name);
        }

        public (MachinePackage Package, string ProgramName, string ProgramSource)? LoadSaved(string name)
        {
            var saved = Find(name);
            if (saved != null) return (saved.Package.Clone(), saved.ProgramName, saved.ProgramSource);
            if (IsBuiltIn(name)) return (BuiltInPackages.Get(name), null, null);
            return null;
        }

        public void Save(MachinePackage package, string programName, string programSource)
        {
            Open = package.Name;
            Discard(package.Name);
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

        public void KeepDraft(MachinePackage package, string programName, string programSource)
        {
            Open = package.Name;
            var draft = new SavedPackage { Package = package.Clone(), ProgramName = programName, ProgramSource = programSource };
            Discard(package.Name);
            var saved = LoadSaved(package.Name);
            if (saved == null) return;
            var baseline = new SavedPackage { Package = saved.Value.Package, ProgramName = saved.Value.ProgramName, ProgramSource = saved.Value.ProgramSource };
            if (!IsUnchanged(draft, baseline)) Drafts.Add(draft);
        }

        public void Discard(string name)
        {
            Drafts.RemoveAll(p => p.Package.Name == name);
        }

        public void Remove(string name)
        {
            Packages.RemoveAll(p => p.Package.Name == name);
            Discard(name);
        }

        private static bool IsAsShipped(SavedPackage saved, bool ignoreLayout = false)
        {
            return IsUnchanged(saved, new SavedPackage { Package = BuiltInPackages.Get(saved.Package.Name) }, ignoreLayout);
        }

        private static bool IsUnchanged(SavedPackage changed, SavedPackage baseline, bool ignoreLayout = false)
        {
            var theirs = baseline.Package.Clone();
            var mine = changed.Package.Clone();
            foreach (var machine in new[] { theirs.Machine, mine.Machine })
            {
                if (ignoreLayout) WithoutLayout(machine);
                else machine.EnsureLayout();
            }
            if (mine.ToJson() != theirs.ToJson()) return false;
            if (changed.ProgramSource == null || changed.ProgramSource == baseline.ProgramSource) return true;
            var first = theirs.Programs.FirstOrDefault();
            var open = theirs.Programs.FirstOrDefault(p => p.Name == changed.ProgramName);
            return open != null ? open.Source == changed.ProgramSource : first != null && changed.ProgramSource == first.Source;
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
