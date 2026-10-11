using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Workbench
{
    public sealed class WorkspaceController
    {
        private const string StorageKey = "exuarch.workspace";
        private const string UnreadableKey = "exuarch.workspace.unreadable";
        private const int SaveDelayMilliseconds = 500;
        public DeviceRegistry Registry { get; }

        private readonly IBrowserStore store;
        private Workspace workspace = new Workspace();
        private bool restored;
        private bool saveScheduled;
        private bool startingOver;
        private string lastStructure;
        private SpeedKind pickedSpeed;

        public WorkspaceController(IBrowserStore store, DeviceRegistry registry = null)
        {
            this.store = store;
            Registry = registry ?? DeviceRegistry.CreateDefault();
            History = new EditHistory(() => Definition.ToJson(), json =>
            {
                ApplyDefinition(MachineDefinition.FromJson(json));
                Changed?.Invoke();
                return Task.CompletedTask;
            });
            LoadPackage(BuiltInPackages.Get(BuiltInPackages.Default.Name));
        }

        public event Action Changed;

        public EditHistory History { get; }
        public Workspace Workspace => workspace;
        public MachinePackage Package { get; private set; }
        public string PackageName => Package.Name;
        public bool IsBuiltIn => BuiltInPackages.All.Any(p => p.Name == PackageName);
        public bool IsEdited => workspace.IsEdited(PackageName);
        public string PackageError { get; set; }
        public string StorageWarning { get; set; }
        public MachineDefinition Definition { get; private set; }
        public string DefinitionJson { get; private set; }
        public string Program { get; private set; }
        public ValidationResult Validation { get; private set; } = ValidationResult.None;
        public PackageProgram CurrentProgram { get; private set; }
        public string NewProgramName { get; set; }
        public string NeedsNote { get; private set; }

        public int ProgramMemorySize
        {
            get
            {
                var built = Validation.Machine;
                if (built?.Definition.ProgramMemory != null && built.Device<MemoryModule>(built.Definition.ProgramMemory) is MemoryModule memory) return memory.Size;
                var device = Definition.FindDevice(Definition.ProgramMemory ?? "");
                return device != null && device.Parameters.TryGetValue("size", out var size) && size.TryGetInt32(out var cells) ? cells : MemoryModule.DefaultSize;
            }
        }

        public bool IsYours(PackageProgram program)
        {
            return !IsBuiltIn || !BuiltInPackages.All.First(p => p.Name == PackageName).Programs.Any(shipped => shipped.Name == program.Name);
        }
        public bool IsCurrent(PackageProgram program) => program == CurrentProgram && (IsYours(program) || program.Source == Program);
        public bool ProgramShips => IsBuiltIn && CurrentProgram != null && !IsYours(CurrentProgram) && CurrentProgram.Source == Program;
        public bool HasEditsToExample => CurrentProgram != null && !IsYours(CurrentProgram) && Program != CurrentProgram.Source;
        public string ProgramTitle
        {
            get
            {
                if (CurrentProgram != null) return IsCurrent(CurrentProgram) ? CurrentProgram.Name : $"{CurrentProgram.Name} (edited)";
                return string.IsNullOrWhiteSpace(Program) ? "No program" : "Untitled program";
            }
        }

        public void LoadPackage(MachinePackage package)
        {
            History.Clear();
            Package = package;
            PackageError = null;
            CurrentProgram = package.Programs.FirstOrDefault();
            Program = CurrentProgram?.Source ?? "";
            NewProgramName = null;
            ApplyDefinition(package.Machine.Clone());
        }

        public bool OpenByName(string name)
        {
            if (name == PackageName) return false;
            SaveOpenPackage();
            if (workspace.Load(name) is not { } saved) return false;
            Open(saved);
            return true;
        }

        private void Open((MachinePackage Package, string ProgramName, string ProgramSource) saved)
        {
            LoadPackage(saved.Package);
            var program = saved.ProgramName == null ? null : Package.Programs.FirstOrDefault(p => p.Name == saved.ProgramName);
            if (program != null) CurrentProgram = program;
            Program = saved.ProgramSource ?? program?.Source ?? Program;
            Rebuild();
        }

        public void CreateMachine(string name, string start)
        {
            var unique = workspace.UniqueName(string.IsNullOrWhiteSpace(name) ? "My machine" : name.Trim());
            SaveOpenPackage();
            var package = start switch
            {
                "empty" => MachineTemplates.Empty(unique),
                "copy" => MachineTemplates.CopyOf(new MachinePackage { Name = Package.Name, Description = Package.Description, Readme = Package.Readme, Machine = Definition.Clone(), Programs = Package.Programs }, unique),
                _ => MachineTemplates.Minimal(unique),
            };
            LoadPackage(package);
            if (start == "minimal") AddProgram("Starter program", MachineTemplates.StarterProgram);
        }

        public bool Replaces(MachinePackage package) => workspace.Find(package.Name) != null || package.Name == PackageName;

        public void Import(MachinePackage package)
        {
            SaveOpenPackage();
            LoadPackage(package);
            MarkChanged();
        }

        public MachinePackage Export()
        {
            var package = Package.Clone();
            package.Machine = Definition.Clone();
            if (!string.IsNullOrWhiteSpace(Program) && !package.Programs.Any(p => p.Source == Program))
                package.Programs.Add(new PackageProgram { Name = UniqueProgramName(package, "My program"), Source = Program });
            return package;
        }
        public MachinePackage Saved(string name) => workspace.Load(name)?.Package;

        public bool CanReset(string name) => Workspace.IsBuiltIn(name) && workspace.IsEdited(name);
        public async Task Reset(string name)
        {
            workspace.Remove(name);
            if (name == PackageName) LoadPackage(BuiltInPackages.Get(name));
            await Persist();
        }

        public async Task Delete(string name)
        {
            workspace.Remove(name);
            if (name == PackageName && workspace.Load(BuiltInPackages.Default.Name) is { } fallback)
            {
                Open(fallback);
                workspace.Open = PackageName;
            }
            await Persist();
        }

        public string StartOverLosses()
        {
            int own = workspace.OwnPackages.Count();
            int edited = BuiltInPackages.All.Count(p => workspace.IsEdited(p.Name));
            var parts = new List<string>();
            if (own > 0) parts.Add($"your {Words.Count(own, "machine")}");
            if (edited > 0) parts.Add($"your changes to {Words.Count(edited, "example")}");
            if (parts.Count == 0) return "";
            return $" {char.ToUpper(parts[0][0])}{string.Join(" and ", parts).Substring(1)} will be lost; Export first to keep {(own + edited == 1 ? "it" : "them")}.";
        }
        public async Task StartOver()
        {
            startingOver = true;
            await store.ResetAll();
        }

        public void SaveOpenPackage()
        {
            if (!restored) return;
            var snapshot = Package.Clone();
            snapshot.Machine = Definition.Clone();
            workspace.Save(snapshot, CurrentProgram?.Name, Program);
        }

        public void MarkChanged()
        {
            if (!restored || saveScheduled) return;
            saveScheduled = true;
            _ = SaveSoon();
        }
        private async Task SaveSoon()
        {
            await Task.Delay(SaveDelayMilliseconds);
            saveScheduled = false;
            SaveOpenPackage();
            await Persist();
            Changed?.Invoke();
        }
        private async Task Persist()
        {
            if (startingOver) return;
            var ok = await store.Set(StorageKey, workspace.ToJson());
            StorageWarning = ok ? null : "Your changes could not be saved in this browser (its storage is off or full). Use Export to keep them as a file.";
        }

        public async Task Restore()
        {
            var json = await store.Get(StorageKey);
            if (!Workspace.TryFromJson(json, out workspace))
            {
                await store.Keep(UnreadableKey, json);
                StorageWarning = "What this browser saved last time could not be read, so it was put aside and you start afresh.";
            }
            if (workspace.Open != null && workspace.Load(workspace.Open) is { } saved) Open(saved);
            restored = true;
        }

        private void ApplyDefinition(MachineDefinition definition)
        {
            Definition = definition;
            lastStructure = Structure(definition);
            Definition.EnsureLayout();
            DefinitionJson = Definition.ToJson();
            Validation = ValidationResult.None;
            Rebuild();
        }

        private static string Structure(MachineDefinition definition)
        {
            var copy = definition.Clone();
            foreach (var bus in copy.Buses) bus.Layout = null;
            foreach (var device in copy.Devices) device.Layout = null;
            if (copy.Decoder != null) { copy.Decoder.Layout = null; copy.Decoder.Microcode = null; }
            return copy.ToJson();
        }

        public bool DesignChanged(MachineDefinition definition)
        {
            var structure = Structure(definition);
            bool edited = lastStructure != null && structure != lastStructure;
            lastStructure = structure;
            Definition = definition;
            DefinitionJson = definition.ToJson();
            Rebuild();
            return edited;
        }

        public void MicrocodeChanged(MicrocodeDefinition microcode)
        {
            Definition.Decoder ??= new DecoderDefinition();
            Definition.Decoder.Microcode = microcode;
            DefinitionJson = Definition.ToJson();
            Rebuild();
        }

        public void StartMicrocode()
        {
            History.Record();
            MicrocodeChanged(new MicrocodeDefinition { Name = Definition.Name, Fetch = new InstructionDefinition { Mnemonic = "FTC", Steps = { new MicroStep() } } });
        }

        public void JsonChanged(string json)
        {
            DefinitionJson = json;
            try
            {
                var parsed = MachineDefinition.FromJson(json);
                History.Record();
                Definition = parsed;
                Definition.EnsureLayout();
                Validation = ValidationResult.None;
                Rebuild();
            }
            catch (MachineDefinitionException e)
            {
                Validation = Validation.WithParseErrors(e.Errors.ToList());
            }
        }

        public void LoadExample(PackageProgram example)
        {
            Program = example.Source;
            CurrentProgram = example;
            pickedSpeed = SpeedKind.Any;
            NeedsNote = null;
            Rebuild();
        }

        public void ProgramChanged(string program)
        {
            Program = program;
            if (CurrentProgram != null && IsYours(CurrentProgram)) CurrentProgram.Source = program;
            Rebuild();
        }

        public void CreateProgram()
        {
            var name = string.IsNullOrWhiteSpace(NewProgramName) ? "My program" : NewProgramName.Trim();
            NewProgramName = null;
            AddProgram(name, $"; {name}\n");
        }

        private static string UniqueProgramName(MachinePackage package, string name)
        {
            var unique = name;
            for (int n = 2; package.Programs.Any(p => p.Name == unique); n++) unique = $"{name} {n}";
            return unique;
        }
        private void AddProgram(string name, string source)
        {
            var program = new PackageProgram { Name = UniqueProgramName(Package, name), Source = source };
            Package.Programs.Add(program);
            LoadExample(program);
        }

        public SpeedNeed Speed => SpeedNeed.Of(CurrentProgram?.Needs, pickedSpeed);

        public void SetNeedsKeypad(bool keypad)
        {
            ChangeNeeds(needs => needs.Keypad = keypad ? true : null);
        }
        public void SetSpeedKind(SpeedKind kind)
        {
            var hz = Speed.Hz;
            pickedSpeed = kind;
            SetSpeed(new SpeedNeed(kind, hz));
        }
        public void SetSpeedHz(string text)
        {
            SetSpeed(Speed with { Hz = int.TryParse(text, out var hz) && hz > 0 ? hz : null });
        }
        private void SetSpeed(SpeedNeed speed)
        {
            NeedsNote = null;
            if (speed.Kind == SpeedKind.Exact && speed.Hz is int requested)
            {
                speed = speed with { Hz = ClockSlider.Nearest(requested) };
                if (speed.Hz != requested) NeedsNote = $"The speed slider can not be set to exactly {requested} Hz, so {speed.Hz} Hz it is.";
            }
            ChangeNeeds(speed.ApplyTo);
        }
        private void ChangeNeeds(Action<ProgramNeeds> change)
        {
            if (CurrentProgram == null) return;
            var needs = CurrentProgram.Needs ?? new ProgramNeeds();
            change(needs);
            CurrentProgram.Needs = needs.IsEmpty() ? null : needs;
            MarkChanged();
        }

        public void Rebuild()
        {
            Package.Machine = Definition;
            MarkChanged();
            Validation = ValidationResult.Of(Definition, Program, Validation.ParseErrors, Registry);
        }
    }
}
