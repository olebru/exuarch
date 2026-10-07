using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Exuarch.Web.Components;

namespace Exuarch.Web.Pages
{
    public partial class ComputerSIM : IDisposable
    {
        // The same names README links use. The machine's JSON is in the drawer, under This machine.
        private static readonly string[] Tabs = ReadmeLinks.Tabs;
        private static readonly DeviceRegistry Registry = DeviceRegistry.CreateDefault();

        [Microsoft.AspNetCore.Components.Inject] private IJSRuntime JS { get; set; }
        [Microsoft.AspNetCore.Components.Inject] private HelpService Help { get; set; }
        [Microsoft.AspNetCore.Components.Inject] private Analytics Analytics { get; set; }

        private string ActiveTab = "Hardware design";
        // The getting started drawer: guides, the example packages and the note of the machine that is open.
        private bool drawerOpen;
        private string drawerSection = "Handbook";
        // The handbook page in the drawer; null shows the contents.
        private (string Kind, string Target)? drawerPage;
        // The package the machine and the example programs came from, as it was loaded.
        private MachinePackage Package;
        private string PackageName { get { return Package.Name; } }
        private bool IsBuiltIn { get { return BuiltInPackages.All.Any(p => p.Name == PackageName); } }

        // ---- Kept in the browser: every change is saved shortly after it is made ----
        private const string StorageKey = "exuarch.workspace";
        private const string UnreadableKey = "exuarch.workspace.unreadable";
        private Workspace workspace = new Workspace();
        // Nothing is saved until what the browser kept has been read, so the default machine can not overwrite it.
        private bool restored;
        private bool saveScheduled;
        private string StorageWarning;
        private bool IsEdited { get { return workspace.IsEdited(PackageName); } }
        // A question before something is thrown away: the message, the button's label, and what it does.
        private (string Message, string Action, Func<Task> Run)? pendingConfirm;
        private string PackageError;
        private Machine C;
        // The whole machine, microcode included (Definition.Decoder.Microcode).
        private MachineDefinition Definition;
        private string DefinitionJson;
        private string Program;
        private IReadOnlyList<string> DefinitionErrors = Array.Empty<string>();
        private IReadOnlyList<MicrocodeDiagnostic> MicrocodeDiagnostics = Array.Empty<MicrocodeDiagnostic>();
        private List<string> ParseErrors = new List<string>();
        private List<string> ProgramErrors = new List<string>();
        private readonly EditHistory History;
        private string focusDevice;
        private string focusMnemonic;
        private int focusStep;
        private int focusVersion;

        public ComputerSIM()
        {
            History = new EditHistory(() => Definition.ToJson(), json =>
            {
                ApplyDefinition(MachineDefinition.FromJson(json));
                StateHasChanged();
                return System.Threading.Tasks.Task.CompletedTask;
            });
            LoadPackage(BuiltInPackages.Get(BuiltInPackages.Default.Name));
        }

        // Cells in the program memory, from the machine when it builds, otherwise its size parameter.
        private int ProgramMemorySize
        {
            get
            {
                if (C?.Definition.ProgramMemory != null && C.Device<MemoryModule>(C.Definition.ProgramMemory) is MemoryModule memory) return memory.Size;
                var device = Definition.FindDevice(Definition.ProgramMemory ?? "");
                return device != null && device.Parameters.TryGetValue("size", out var size) && size.TryGetInt32(out var cells) ? cells : MemoryModule.DefaultSize;
            }
        }

        private int MicrocodeErrorCount { get { return MicrocodeDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error); } }
        private int MicrocodeWarningCount { get { return MicrocodeDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning); } }
        private IEnumerable<string> AllErrors
        {
            get
            {
                return ParseErrors.Concat(DefinitionErrors)
                    .Concat(MicrocodeDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))
                    .Concat(ProgramErrors);
            }
        }

        private void ApplyDefinition(MachineDefinition definition)
        {
            Definition = definition;
            lastStructure = Structure(definition);
            Definition.EnsureLayout();
            DefinitionJson = Definition.ToJson();
            ParseErrors = new List<string>();
            Rebuild();
        }

        // The package program in the editor. Your programs (every program of your own machine, and any made with
        // New program) take the edits; a built in example stays as it is, and the title then says "(edited)".
        private PackageProgram currentProgram;
        private bool IsYours(PackageProgram program)
        {
            return !IsBuiltIn || !BuiltInPackages.All.First(p => p.Name == PackageName).Programs.Any(shipped => shipped.Name == program.Name);
        }
        private bool IsCurrent(PackageProgram program) { return program == currentProgram && (IsYours(program) || program.Source == Program); }
        private string ProgramTitle
        {
            get
            {
                if (currentProgram != null) return IsCurrent(currentProgram) ? currentProgram.Name : $"{currentProgram.Name} (edited)";
                return string.IsNullOrWhiteSpace(Program) ? "No program" : "Untitled program";
            }
        }

        // Opening a machine starts its undo history afresh (see EditHistory).
        private void LoadPackage(MachinePackage package)
        {
            History?.Clear();
            Package = package;
            PackageError = null;
            currentProgram = package.Programs.FirstOrDefault();
            Program = currentProgram?.Source ?? "";
            newProgramName = null;
            ApplyDefinition(package.Machine.Clone());
        }

        // Opens a package as it was left, built in or the user's own.
        private void OpenByName(string name)
        {
            if (name == PackageName) return;
            Remember();
            if (workspace.Load(name) is not { } saved) return;
            Open(saved);
            drawerSection = "This machine";
        }

        // A saved package, with the program that was open and the text that was in the editor.
        private void Open((MachinePackage Package, string ProgramName, string ProgramSource) saved)
        {
            LoadPackage(saved.Package);
            var program = saved.ProgramName == null ? null : Package.Programs.FirstOrDefault(p => p.Name == saved.ProgramName);
            if (program != null) currentProgram = program;
            if (saved.ProgramSource != null) Program = saved.ProgramSource;
            else if (program != null) Program = program.Source;
            Rebuild();
        }

        // The open package, as it is now, into the workspace in memory.
        private void Remember()
        {
            if (!restored) return;
            var snapshot = Package.Clone();
            snapshot.Machine = Definition.Clone();
            workspace.Save(snapshot, currentProgram?.Name, Program);
        }

        // Called after every change: saves half a second later, so a burst of edits is one save.
        private void MarkChanged()
        {
            if (!restored || saveScheduled) return;
            saveScheduled = true;
            _ = SaveSoon();
        }
        private async Task SaveSoon()
        {
            await Task.Delay(500);
            saveScheduled = false;
            await SaveNow();
        }
        private async Task SaveNow()
        {
            Remember();
            await Persist();
            StateHasChanged();
        }
        private async Task Persist()
        {
            if (startingOver) return;
            var ok = await JS.InvokeAsync<bool>("exuarchStore.set", StorageKey, workspace.ToJson());
            StorageWarning = ok ? null : "Your changes could not be saved in this browser (its storage is off or full). Use Export to keep them as a file.";
        }

        // What the browser kept, opened where the user left off. A save that can not be read is put aside, not lost.
        private async Task Restore()
        {
            var json = await JS.InvokeAsync<string>("exuarchStore.get", StorageKey);
            if (!Workspace.TryFromJson(json, out workspace))
            {
                await JS.InvokeVoidAsync("exuarchStore.set", UnreadableKey, json);
                StorageWarning = "What this browser saved last time could not be read, so it was put aside and you start afresh.";
            }
            if (workspace.Open != null && workspace.Load(workspace.Open) is { } saved) Open(saved);
            restored = true;
        }

        private void Ask(string message, string action, Func<Task> run)
        {
            pendingConfirm = (message, action, run);
        }
        private async Task Confirm()
        {
            var run = pendingConfirm?.Run;
            pendingConfirm = null;
            if (run != null) await run();
        }

        // A "?" somewhere in the editors: open that handbook page.
        private void OnHelp(string kind, string target)
        {
            InvokeAsync(() =>
            {
                FollowLink((kind, target));
                StateHasChanged();
            });
        }

        public void Dispose()
        {
            Help.Requested -= OnHelp;
            disposed = true;
        }

        private void ToggleDrawer(string section)
        {
            drawerOpen = !drawerOpen || drawerSection != section;
            drawerSection = section;
        }
        // ---- New machine ----
        private static readonly (string Value, string Title, string Description)[] NewStarts =
        {
            ("minimal", "Minimal CPU", "A bus, program counter, memory, instruction register, status register and clock, with fetch, NOP, JMP and HLT. It runs straight away."),
            ("empty", "Empty", "One bus and nothing else. Add the devices, decoder and microcode yourself."),
            ("copy", "Copy of the current machine", "Everything in the machine you have open now, with its programs, under the new name."),
        };
        private bool newDialog;
        private string newName = "My machine";
        private string newStart = "minimal";
        private Microsoft.AspNetCore.Components.ElementReference newNameInput;
        private bool focusNewName;

        private void OpenNewDialog()
        {
            newDialog = true;
            focusNewName = true;
        }
        // ---- The splash screen, on the very first visit ----

        // A built in machine and the program that shows it off, with a few words on what it is.
        private static readonly (string Package, string Program, string What)[] Showcases =
        {
            ("TURBO-16", "A spinning cube", "a shaded 3D cube from a rasterizer"),
            ("DSP-16", "Mandelbrot, 80 x 60", "the Mandelbrot set, with a hardware multiplier"),
            ("IRQ-16", "Falling blocks", "a game run on interrupts"),
        };
        private bool splash;
        private bool focusSplash;
        private Microsoft.AspNetCore.Components.ElementReference splashGo;
        // How the Run view should start the next machine it shows: "slow", "max" or null for not at all.
        private string autoStart;

        // Opens a built in machine with one of its programs and runs it.
        private void Showcase(string package, string program, string speed)
        {
            splash = false;
            OpenByName(package);
            if (Package.Programs.FirstOrDefault(p => p.Name == program) is { } example) LoadExample(example);
            ActiveTab = "Run";
            autoStart = speed;
            _ = Analytics.Splash(package);
        }
        private void CloseSplash(string then)
        {
            splash = false;
            if (then == "guide")
            {
                drawerOpen = true;
                drawerSection = "Handbook";
                drawerPage = ("guide", "getting-started");
            }
            _ = Analytics.Splash(then ?? "closed");
        }
        private void SplashKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Escape") CloseSplash(null);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // The very first visit shows the splash screen once; after that it stays away.
            if (firstRender)
            {
                Help.Requested += OnHelp;
                await Restore();
                StateHasChanged();
                if (!await JS.InvokeAsync<bool>("exuarchWelcome.seen"))
                {
                    splash = true;
                    focusSplash = true;
                    StateHasChanged();
                }
            }
            if (focusSplash && splash)
            {
                focusSplash = false;
                await splashGo.FocusAsync();
            }
            if (focusNewName && newDialog)
            {
                focusNewName = false;
                await newNameInput.FocusAsync();
            }
            if (focusNewProgram && newProgramName != null)
            {
                focusNewProgram = false;
                await newProgramInput.FocusAsync();
            }
            await TrackChanges(firstRender);
        }

        // What the page shows, for the usage statistics (see Analytics). It is told after every render, and every few
        // seconds by a heartbeat, so a page read or an error left standing counts while nothing on this page renders.
        private bool disposed;
        private async Task TrackChanges(bool firstRender)
        {
            if (firstRender) _ = Heartbeat();
            await ObserveView();
        }
        // Task.Delay, started from a render, carries on in the component's own context; a timer thread would need
        // InvokeAsync, and System.Threading.Timer does not fire in the browser build.
        private async Task Heartbeat()
        {
            while (!disposed)
            {
                await Task.Delay(5000);
                if (!disposed) await ObserveView();
            }
        }
        private async Task ObserveView()
        {
            bool active = await Analytics.IdleSeconds() < 60;
            string errors = ParseErrors.Count > 0 || DefinitionErrors.Count > 0 ? "hardware"
                : MicrocodeErrorCount > 0 ? "microcode"
                : ProgramErrors.Count > 0 ? "program" : null;
            await Analytics.Observe(new Analytics.View
            {
                Tab = ActiveTab,
                Machine = PackageName,
                MachineIsBuiltIn = IsBuiltIn,
                ProgramShips = IsBuiltIn && currentProgram != null && !IsYours(currentProgram) && currentProgram.Source == Program,
                Page = drawerOpen && drawerSection == "Handbook" ? drawerPage : null,
                Errors = errors,
                Active = active,
            });
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

        private void NewDialogKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Escape") newDialog = false;
            else if (e.Key == "Enter") CreateMachine();
        }
        // A name already used by another package gets a number.
        private void CreateMachine()
        {
            var name = string.IsNullOrWhiteSpace(newName) ? "My machine" : newName.Trim();
            var unique = workspace.UniqueName(name);
            Remember();
            var package = newStart switch
            {
                "empty" => MachineTemplates.Empty(unique),
                "copy" => MachineTemplates.CopyOf(new MachinePackage { Name = Package.Name, Description = Package.Description, Readme = Package.Readme, Machine = Definition.Clone(), Programs = Package.Programs }, unique),
                _ => MachineTemplates.Minimal(unique),
            };
            LoadPackage(package);
            _ = Analytics.NewMachine(newStart);
            if (newStart == "minimal") AddProgram("Starter program", MachineTemplates.StarterProgram);
            newDialog = false;
            ActiveTab = "Hardware design";
        }

        // A built in package back the way it ships: its changes are forgotten, after asking.
        private void ResetPackage(string name)
        {
            if (!Workspace.IsBuiltIn(name) || !workspace.IsEdited(name)) return;
            Ask($"Reset {name} to the way it ships? Your changes to it will be lost; Export first to keep them.", "Reset", async () =>
            {
                workspace.Forget(name);
                if (name == PackageName)
                {
                    LoadPackage(BuiltInPackages.Get(name));
                }
                await Persist();
            });
        }

        // One of the user's own packages, removed from the browser after asking. If it is open, the default opens.
        private void DeletePackage(string name)
        {
            if (Workspace.IsBuiltIn(name)) return;
            Ask($"Delete {name} from this browser? Export it first to keep a copy.", "Delete", async () =>
            {
                workspace.Forget(name);
                if (name == PackageName && workspace.Load(BuiltInPackages.Default.Name) is { } fallback)
                {
                    Open(fallback);
                    workspace.Open = PackageName;
                }
                await Persist();
            });
        }

        // Everything back to the way it was on the first visit, after asking: the user's own machines and changes go,
        // and so do the theme and the layout. A save still waiting must not write the workspace back.
        private bool startingOver;
        private void StartOver()
        {
            int own = workspace.OwnPackages.Count();
            int edited = BuiltInPackages.All.Count(p => workspace.IsEdited(p.Name));
            var parts = new List<string>();
            if (own > 0) parts.Add($"your {Count(own, "machine")}");
            if (edited > 0) parts.Add($"your changes to {Count(edited, "example")}");
            var lost = parts.Count == 0 ? "" : $" {char.ToUpper(parts[0][0])}{string.Join(" and ", parts).Substring(1)} will be lost; Export first to keep {(own + edited == 1 ? "it" : "them")}.";
            Ask($"Reset everything to the way it was on your first visit?{lost}", "Reset everything", async () =>
            {
                startingOver = true;
                await JS.InvokeVoidAsync("exuarchStore.resetAll");
            });
        }
        private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

        // A package file, opened and kept. A file with the name of a package that is already here replaces it,
        // after asking; one for a built in package becomes that package's changes.
        private async Task ImportPackage(InputFileChangeEventArgs e)
        {
            MachinePackage package;
            try
            {
                using var reader = new StreamReader(e.File.OpenReadStream(maxAllowedSize: 16 * 1024 * 1024));
                var json = await reader.ReadToEndAsync();
                package = IsMachineFile(json)
                    ? new MachinePackage { Machine = MachineDefinition.FromJson(json) }
                    : MachinePackage.FromJson(json);
                if (string.IsNullOrWhiteSpace(package.Name)) package.Name = package.Machine.Name;
                if (string.IsNullOrWhiteSpace(package.Name)) package.Name = Path.GetFileNameWithoutExtension(e.File.Name).Replace(".machine", "");
            }
            catch (Exception ex) when (ex is MachineDefinitionException || ex is IOException)
            {
                PackageError = $"Could not import {e.File.Name}: {ex.Message}";
                return;
            }
            Task Take()
            {
                Remember();
                LoadPackage(package);
                MarkChanged();
                _ = Analytics.Import();
                return Task.CompletedTask;
            }
            if (workspace.Find(package.Name) != null || package.Name == PackageName)
            {
                Ask($"Replace {package.Name} with the one in {e.File.Name}? The {package.Name} you have now will be lost.", "Replace", Take);
                return;
            }
            await Take();
        }

        // A machine definition on its own has buses or devices at the top, where a package has "machine".
        private static bool IsMachineFile(string json)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = document.RootElement;
                return root.ValueKind == System.Text.Json.JsonValueKind.Object && !root.TryGetProperty("machine", out _)
                    && (root.TryGetProperty("buses", out _) || root.TryGetProperty("devices", out _));
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        // The open package as it is now, as a file; a program in the editor that is not one of the package's is
        // added to it.
        private async Task ExportPackage()
        {
            var package = Package.Clone();
            package.Machine = Definition.Clone();
            if (!string.IsNullOrWhiteSpace(Program) && !package.Programs.Any(p => p.Source == Program))
                package.Programs.Add(new PackageProgram { Name = UniqueProgramName(package, "My program"), Source = Program });
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{package.Name}.json", package.ToJson());
            await Analytics.Export(package.Name);
        }

        // Any package here, as a file, without opening it.
        private async Task ExportByName(string name)
        {
            if (name == PackageName) { await ExportPackage(); return; }
            if (workspace.Load(name) is not { } saved) return;
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{name}.json", saved.Package.ToJson());
        }

        // What the hardware was last time it changed: the editor changes the definition in place, so the one it hands
        // over can not be compared with the one before.
        private string lastStructure;
        private void OnDesignChanged(MachineDefinition definition)
        {
            var structure = Structure(definition);
            if (lastStructure != null && structure != lastStructure) _ = Analytics.EditedHardware();
            lastStructure = structure;
            Definition = definition;
            DefinitionJson = definition.ToJson();
            Rebuild();
        }

        private void OnMicrocodeChanged(MicrocodeDefinition microcode)
        {
            _ = Analytics.EditedMicrocode();
            Definition.Decoder ??= new DecoderDefinition();
            Definition.Decoder.Microcode = microcode;
            DefinitionJson = Definition.ToJson();
            Rebuild();
        }

        private void StartMicrocode()
        {
            History.Record();
            OnMicrocodeChanged(new MicrocodeDefinition { Name = Definition.Name, Fetch = new InstructionDefinition { Mnemonic = "FTC", Steps = { new MicroStep() } } });
        }

        // A JSON edit that parses replaces the model; one that does not keeps the last good model in the editors.
        private void OnJsonChanged(string json)
        {
            DefinitionJson = json;
            try
            {
                var parsed = MachineDefinition.FromJson(json);
                History.Record();
                Definition = parsed;
                Definition.EnsureLayout();
                ParseErrors = new List<string>();
                Rebuild();
            }
            catch (MachineDefinitionException e)
            {
                C = null;
                ParseErrors = e.Errors.ToList();
            }
        }

        private void LoadExample(PackageProgram example)
        {
            Program = example.Source;
            currentProgram = example;
            Rebuild();
        }

        // Picking another program replaces the editor's text. Edits to one of your programs are already in it; edits
        // to a built in example are only in the editor, so they are not thrown away without asking.
        private void PickProgram(PackageProgram example)
        {
            if (example == currentProgram && IsCurrent(example)) return;
            if (currentProgram != null && !IsYours(currentProgram) && Program != currentProgram.Source)
            {
                Ask($"Throw away your changes to {currentProgram.Name}? To keep them, use ＋ New program and paste them in first.", "Throw away", () =>
                {
                    LoadExample(example);
                    return Task.CompletedTask;
                });
                return;
            }
            LoadExample(example);
        }

        private void OnProgramChanged(string program)
        {
            Program = program;
            if (currentProgram != null && IsYours(currentProgram)) currentProgram.Source = program;
            Rebuild();
        }

        // What the open program needs from the Run view; a program that needs nothing has no needs at all.
        private void SetNeedsKeypad(bool keypad)
        {
            ChangeNeeds(needs => needs.Keypad = keypad ? true : null);
        }
        private void SetNeedsMinHz(string text)
        {
            ChangeNeeds(needs => needs.MinHz = int.TryParse(text, out var hz) && hz > 0 ? hz : null);
        }
        private void ChangeNeeds(Action<ProgramNeeds> change)
        {
            if (currentProgram == null) return;
            var needs = currentProgram.Needs ?? new ProgramNeeds();
            change(needs);
            currentProgram.Needs = needs.IsEmpty() ? null : needs;
            MarkChanged();
        }

        // ---- New program: named in place in the program bar, then added to the package and opened ----
        // Null while the name field is closed.
        private string newProgramName;
        private Microsoft.AspNetCore.Components.ElementReference newProgramInput;
        private bool focusNewProgram;

        private void StartNewProgram()
        {
            newProgramName = "";
            focusNewProgram = true;
        }

        private void CreateProgram()
        {
            var name = string.IsNullOrWhiteSpace(newProgramName) ? "My program" : newProgramName.Trim();
            newProgramName = null;
            AddProgram(name, $"; {name}\n");
        }

        private void NewProgramKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter") CreateProgram();
            else if (e.Key == "Escape") newProgramName = null;
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

        private void OpenMicrocode((string Mnemonic, int Step) target)
        {
            focusMnemonic = target.Mnemonic;
            focusStep = target.Step;
            focusVersion++;
            ActiveTab = "Microcode";
        }

        private void OpenDevice(string deviceId)
        {
            focusDevice = deviceId;
            focusVersion++;
            ActiveTab = "Hardware design";
        }

        // A link in the package README: open the device, the instruction, the program or the tab it names.
        private void FollowLink((string Kind, string Target) link)
        {
            switch (link.Kind)
            {
                case "device": OpenDevice(link.Target); break;
                case "instruction": OpenMicrocode((link.Target, 0)); break;
                case "program":
                    var example = Package.Programs.FirstOrDefault(p => p.Name == link.Target);
                    if (example != null) PickProgram(example);
                    ActiveTab = "Program";
                    break;
                case "tab":
                    if (Tabs.Contains(link.Target)) ActiveTab = link.Target;
                    break;
                case "package":
                    OpenByName(link.Target);
                    break;
                case "guide":
                case "reference":
                    drawerOpen = true;
                    drawerSection = "Handbook";
                    drawerPage = link;
                    break;
            }
        }

        // Validates the definition, then the microcode against it, then builds a machine to assemble the program.
        private void Rebuild()
        {
            Package.Machine = Definition;
            MarkChanged();
            DefinitionErrors = Machine.ValidateDefinition(Definition, Registry);
            var microcode = Definition.Decoder?.Microcode;
            MicrocodeDiagnostics = microcode == null
                ? new[] { new MicrocodeDiagnostic { Severity = DiagnosticSeverity.Error, Message = "the machine has no microcode (decoder.microcode)." } }
                : MicrocodeValidator.Validate(microcode, Definition, Registry);
            ProgramErrors = new List<string>();
            C = null;
            if (ParseErrors.Count > 0 || DefinitionErrors.Count > 0 || MicrocodeErrorCount > 0) return;
            try
            {
                C = new Machine(Definition.Clone(), Program, Registry);
            }
            catch (MachineDefinitionException e)
            {
                ProgramErrors.AddRange(e.Errors);
            }
            catch (Exception e)
            {
                ProgramErrors.Add(e.Message);
            }
        }
    }
}
