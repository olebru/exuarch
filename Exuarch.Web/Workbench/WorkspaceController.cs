using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Workbench
{
    // The package that is open and everything done to it: the machine with its microcode, the program in the editor,
    // what checking them found, the undo history, and keeping all of it, with the user's other packages, between
    // visits. The page shows it; nothing here needs the browser except through the store.
    public sealed class WorkspaceController
    {
        private const string StorageKey = "exuarch.workspace";
        private const string UnreadableKey = "exuarch.workspace.unreadable";
        // The device types machines are built from; the page gets it from dependency injection.
        public DeviceRegistry Registry { get; }

        private readonly IBrowserStore store;
        private Workspace workspace = new Workspace();
        // Nothing is saved until what the browser kept has been read, so the default machine can not overwrite it.
        private bool restored;
        private bool saveScheduled;
        // Starting over reloads the page; a save still waiting must not write the workspace back.
        private bool startingOver;
        // What the hardware was last time it changed: the editor changes the definition in place, so the one it hands
        // over can not be compared with the one before.
        private string lastStructure;
        // The kind of clock speed picked before a speed is typed, since needs without a speed say nothing about it.
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

        // Something changed on its own: a save finished, or an undo put another definition in place.
        public event Action Changed;

        // Undo history shared by the machine and microcode editors.
        public EditHistory History { get; }
        // What is kept in the browser: the user's own packages, and the changes to the examples.
        public Workspace Workspace => workspace;
        // The package the machine and the example programs came from, as it was loaded.
        public MachinePackage Package { get; private set; }
        public string PackageName => Package.Name;
        public bool IsBuiltIn => BuiltInPackages.All.Any(p => p.Name == PackageName);
        public bool IsEdited => workspace.IsEdited(PackageName);
        public string PackageError { get; set; }
        public string StorageWarning { get; set; }
        // The whole machine, microcode included (Definition.Decoder.Microcode), and as JSON.
        public MachineDefinition Definition { get; private set; }
        public string DefinitionJson { get; private set; }
        public string Program { get; private set; }
        public ValidationResult Validation { get; private set; } = ValidationResult.None;
        // The package program in the editor. Your programs (every program of your own machine, and any made with New
        // program) take the edits; a built in example stays as it is, and the title then says "(edited)".
        public PackageProgram CurrentProgram { get; private set; }
        // The name typed for a new program; null while the field is closed.
        public string NewProgramName { get; set; }
        // Why an exact speed was moved to one the slider can be set to.
        public string NeedsNote { get; private set; }

        // Cells in the program memory, from the machine when it builds, otherwise its size parameter.
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
        // Whether the program in the editor is a built in example as it ships, for the usage statistics.
        public bool ProgramShips => IsBuiltIn && CurrentProgram != null && !IsYours(CurrentProgram) && CurrentProgram.Source == Program;
        // Edits to a built in example are only in the editor: picking another program would throw them away.
        public bool HasEditsToExample => CurrentProgram != null && !IsYours(CurrentProgram) && Program != CurrentProgram.Source;
        public string ProgramTitle
        {
            get
            {
                if (CurrentProgram != null) return IsCurrent(CurrentProgram) ? CurrentProgram.Name : $"{CurrentProgram.Name} (edited)";
                return string.IsNullOrWhiteSpace(Program) ? "No program" : "Untitled program";
            }
        }

        // ---- Opening packages ----

        // Opening a machine starts its undo history afresh (see EditHistory).
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

        // Opens a package as it was left, built in or the user's own; false when it is open already or not here.
        public bool OpenByName(string name)
        {
            if (name == PackageName) return false;
            Remember();
            if (workspace.Load(name) is not { } saved) return false;
            Open(saved);
            return true;
        }

        // A saved package, with the program that was open and the text that was in the editor.
        private void Open((MachinePackage Package, string ProgramName, string ProgramSource) saved)
        {
            LoadPackage(saved.Package);
            var program = saved.ProgramName == null ? null : Package.Programs.FirstOrDefault(p => p.Name == saved.ProgramName);
            if (program != null) CurrentProgram = program;
            Program = saved.ProgramSource ?? program?.Source ?? Program;
            Rebuild();
        }

        // A machine of the user's own, under a name no other package has: from a template, or a copy of this one.
        public void CreateMachine(string name, string start)
        {
            var unique = workspace.UniqueName(string.IsNullOrWhiteSpace(name) ? "My machine" : name.Trim());
            Remember();
            var package = start switch
            {
                "empty" => MachineTemplates.Empty(unique),
                "copy" => MachineTemplates.CopyOf(new MachinePackage { Name = Package.Name, Description = Package.Description, Readme = Package.Readme, Machine = Definition.Clone(), Programs = Package.Programs }, unique),
                _ => MachineTemplates.Minimal(unique),
            };
            LoadPackage(package);
            if (start == "minimal") AddProgram("Starter program", MachineTemplates.StarterProgram);
        }

        // An imported package replaces one of the same name.
        public bool Replaces(MachinePackage package) => workspace.Find(package.Name) != null || package.Name == PackageName;

        public void Import(MachinePackage package)
        {
            Remember();
            LoadPackage(package);
            MarkChanged();
        }

        // The open package as it is now, for a file; a program in the editor that is not one of the package's is added.
        public MachinePackage Export()
        {
            var package = Package.Clone();
            package.Machine = Definition.Clone();
            if (!string.IsNullOrWhiteSpace(Program) && !package.Programs.Any(p => p.Source == Program))
                package.Programs.Add(new PackageProgram { Name = UniqueProgramName(package, "My program"), Source = Program });
            return package;
        }
        // Another package as it was saved, or null when there is none of that name.
        public MachinePackage Saved(string name) => workspace.Load(name)?.Package;

        // A built in package back the way it ships: its changes are forgotten.
        public bool CanReset(string name) => Workspace.IsBuiltIn(name) && workspace.IsEdited(name);
        public async Task Reset(string name)
        {
            workspace.Forget(name);
            if (name == PackageName) LoadPackage(BuiltInPackages.Get(name));
            await Persist();
        }

        // One of the user's own packages, removed from the browser. If it is open, the default opens.
        public async Task Delete(string name)
        {
            workspace.Forget(name);
            if (name == PackageName && workspace.Load(BuiltInPackages.Default.Name) is { } fallback)
            {
                Open(fallback);
                workspace.Open = PackageName;
            }
            await Persist();
        }

        // What starting over throws away, in words: " Your 2 machines and your changes to 1 example will be lost; ...".
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
        // Everything back to the way it was on the first visit: the user's own machines and changes go, and so do the
        // theme and the layout.
        public async Task StartOver()
        {
            startingOver = true;
            await store.ResetAll();
        }

        // ---- Kept in the browser: every change is saved shortly after it is made ----

        // The open package, as it is now, into the workspace in memory.
        public void Remember()
        {
            if (!restored) return;
            var snapshot = Package.Clone();
            snapshot.Machine = Definition.Clone();
            workspace.Save(snapshot, CurrentProgram?.Name, Program);
        }

        // Called after every change: saves half a second later, so a burst of edits is one save.
        public void MarkChanged()
        {
            if (!restored || saveScheduled) return;
            saveScheduled = true;
            _ = SaveSoon();
        }
        private async Task SaveSoon()
        {
            await Task.Delay(500);
            saveScheduled = false;
            Remember();
            await Persist();
            Changed?.Invoke();
        }
        private async Task Persist()
        {
            if (startingOver) return;
            var ok = await store.Set(StorageKey, workspace.ToJson());
            StorageWarning = ok ? null : "Your changes could not be saved in this browser (its storage is off or full). Use Export to keep them as a file.";
        }

        // What the browser kept, opened where the user left off. A save that can not be read is put aside, not lost.
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

        // ---- The machine ----

        private void ApplyDefinition(MachineDefinition definition)
        {
            Definition = definition;
            lastStructure = Structure(definition);
            Definition.EnsureLayout();
            DefinitionJson = Definition.ToJson();
            Validation = ValidationResult.None;
            Rebuild();
        }

        // A change to what is built, not to where it is drawn: moving a card does not count as editing the hardware.
        private static string Structure(MachineDefinition definition)
        {
            var copy = definition.Clone();
            foreach (var bus in copy.Buses) bus.Layout = null;
            foreach (var device in copy.Devices) device.Layout = null;
            if (copy.Decoder != null) { copy.Decoder.Layout = null; copy.Decoder.Microcode = null; }
            return copy.ToJson();
        }

        // The hardware editor's change; true when it changed what is built, not only where it is drawn.
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

        // Microcode for a machine that has none: an empty fetch routine.
        public void StartMicrocode()
        {
            History.Record();
            MicrocodeChanged(new MicrocodeDefinition { Name = Definition.Name, Fetch = new InstructionDefinition { Mnemonic = "FTC", Steps = { new MicroStep() } } });
        }

        // A JSON edit that parses replaces the model; one that does not keeps the last good model in the editors.
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

        // ---- The program ----

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

        // A new program, named in place in the program bar, then added to the package and opened.
        public void CreateProgram()
        {
            var name = string.IsNullOrWhiteSpace(NewProgramName) ? "My program" : NewProgramName.Trim();
            NewProgramName = null;
            AddProgram(name, $"; {name}\n");
        }

        // A name the package already uses gets a number.
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

        // ---- What the open program needs from the Run view; a program that needs nothing has no needs at all ----

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
        // The slider can not be set to every speed, so an exact one is moved to the nearest it can.
        private void SetSpeed(SpeedNeed speed)
        {
            NeedsNote = null;
            if (speed.Kind == SpeedKind.Exact && speed.Hz is int wanted)
            {
                speed = speed with { Hz = ClockSlider.Nearest(wanted) };
                if (speed.Hz != wanted) NeedsNote = $"The speed slider can not be set to exactly {wanted} Hz, so {speed.Hz} Hz it is.";
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

        // Validates the definition, then the microcode against it, then builds a machine to assemble the program.
        public void Rebuild()
        {
            Package.Machine = Definition;
            MarkChanged();
            Validation = ValidationResult.Of(Definition, Program, Validation.ParseErrors, Registry);
        }
    }
}
