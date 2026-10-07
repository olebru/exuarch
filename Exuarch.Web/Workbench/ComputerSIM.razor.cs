using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Exuarch.Web.Workbench
{
    // The workspace: a tab for each part of the machine, the package bar, the dialogs and the getting started drawer.
    // What is open and every change to it are in WorkspaceController; this page shows it and asks before anything is
    // thrown away.
    public partial class ComputerSIM : IDisposable
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private HelpService Help { get; set; }
        [Inject] private Analytics Analytics { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }

        private WorkspaceController workspace;
        private WorkspaceTab[] tabs;
        private WorkspaceTab activeTab;
        private LinkRouter links;
        // The getting started drawer: guides, the example packages and the note of the machine that is open.
        private bool drawerOpen;
        private string drawerSection = "Handbook";
        // The handbook page in the drawer; null shows the contents.
        private (string Kind, string Target)? drawerPage;
        private Confirmation pendingConfirm;
        private bool newDialog;
        private readonly NewMachineForm newMachine = new NewMachineForm();
        // The splash screen, on the very first visit.
        private bool splash;
        // How the Run view should start the next machine it shows: "slow", "max" or null for not at all.
        private string autoStart;
        // What the other views point at in the editors; each request has a new version.
        private string focusDevice;
        private string focusMnemonic;
        private int focusStep;
        private int focusVersion;
        private bool disposed;

        protected override void OnInitialized()
        {
            workspace = new WorkspaceController(new BrowserStore(JS), Registry);
            workspace.Changed += () => InvokeAsync(StateHasChanged);
            // The same names README links use (ReadmeLinks.Tabs).
            tabs = new[]
            {
                new WorkspaceTab("Hardware design", () => new TabBadge(workspace.Validation.DefinitionErrors.Count, "danger"), () => HardwareBody),
                new WorkspaceTab("Microcode", MicrocodeBadge, () => MicrocodeBody),
                new WorkspaceTab("Program", () => new TabBadge(workspace.Validation.ProgramErrors.Count, "danger"), () => ProgramBody),
                new WorkspaceTab("Run", () => TabBadge.None, () => RunBody),
            };
            activeTab = tabs[0];
            links = new LinkRouter()
                .On("device", link => OpenDevice(link.Target))
                .On("instruction", link => OpenMicrocode((link.Target, 0)))
                .On("program", link => OpenProgram(link.Target))
                .On("tab", link => OpenTab(link.Target))
                .On("package", link => OpenByName(link.Target))
                .On("guide", OpenPage)
                .On("reference", OpenPage);
        }

        // Errors, or else warnings.
        private TabBadge MicrocodeBadge()
        {
            var validation = workspace.Validation;
            return validation.MicrocodeErrorCount > 0 ? new TabBadge(validation.MicrocodeErrorCount, "danger") : new TabBadge(validation.MicrocodeWarningCount, "warning");
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
            {
                await ObserveView();
                return;
            }
            Help.Requested += OnHelp;
            await workspace.Restore();
            StateHasChanged();
            // The very first visit shows the splash screen once; after that it stays away.
            if (!await JS.InvokeAsync<bool>("exuarchWelcome.seen"))
            {
                splash = true;
                StateHasChanged();
            }
            _ = Heartbeat();
            await ObserveView();
        }

        public void Dispose()
        {
            Help.Requested -= OnHelp;
            disposed = true;
        }

        // ---- Usage statistics (see Analytics) ----

        // What the page shows. It is told after every render, and every few seconds by a heartbeat, so a page read or
        // an error left standing counts while nothing on this page renders. Task.Delay, started from a render, carries
        // on in the component's own context; a timer thread would need InvokeAsync, and System.Threading.Timer does not
        // fire in the browser build.
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
            await Analytics.Observe(new Analytics.View
            {
                Tab = activeTab.Title,
                Machine = workspace.PackageName,
                MachineIsBuiltIn = workspace.IsBuiltIn,
                ProgramShips = workspace.ProgramShips,
                Page = drawerOpen && drawerSection == "Handbook" ? drawerPage : null,
                Errors = workspace.Validation.FailingStage,
                Active = active,
            });
        }

        // ---- Asking first ----

        private void Ask(string message, string action, Func<Task> run)
        {
            pendingConfirm = new Confirmation(message, action, run);
        }
        private async Task Confirm()
        {
            var run = pendingConfirm?.Run;
            pendingConfirm = null;
            if (run != null) await run();
        }

        // ---- The drawer and links ----

        private void ToggleDrawer(string section)
        {
            drawerOpen = !drawerOpen || drawerSection != section;
            drawerSection = section;
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

        // A link in the package README or the handbook: open the device, the instruction, the program, the tab, the
        // package or the page it names.
        private void FollowLink((string Kind, string Target) link)
        {
            links.Follow(link);
        }

        private void OpenPage((string Kind, string Target) link)
        {
            drawerOpen = true;
            drawerSection = "Handbook";
            drawerPage = link;
        }

        private void OpenTab(string title)
        {
            activeTab = tabs.FirstOrDefault(t => t.Title == title) ?? activeTab;
        }

        private void OpenMicrocode((string Mnemonic, int Step) target)
        {
            focusMnemonic = target.Mnemonic;
            focusStep = target.Step;
            focusVersion++;
            OpenTab("Microcode");
        }

        private void OpenDevice(string deviceId)
        {
            focusDevice = deviceId;
            focusVersion++;
            OpenTab("Hardware design");
        }

        private void OpenProgram(string name)
        {
            var example = workspace.Package.Programs.FirstOrDefault(p => p.Name == name);
            if (example != null) PickProgram(example);
            OpenTab("Program");
        }

        // ---- Packages ----

        // Opens a package as it was left, and shows its note in the drawer.
        private void OpenByName(string name)
        {
            if (workspace.OpenByName(name)) drawerSection = "This machine";
        }

        private void CreateMachine()
        {
            workspace.CreateMachine(newMachine.Name, newMachine.Start);
            _ = Analytics.NewMachine(newMachine.Start);
            newDialog = false;
            OpenTab("Hardware design");
        }

        // A built in package back the way it ships: its changes are forgotten, after asking.
        private void ResetPackage(string name)
        {
            if (!workspace.CanReset(name)) return;
            Ask($"Reset {name} to the way it ships? Your changes to it will be lost; Export first to keep them.", "Reset", () => workspace.Reset(name));
        }

        // One of the user's own packages, removed from the browser after asking. If it is open, the default opens.
        private void DeletePackage(string name)
        {
            if (Workspace.IsBuiltIn(name)) return;
            Ask($"Delete {name} from this browser? Export it first to keep a copy.", "Delete", () => workspace.Delete(name));
        }

        // Everything back to the way it was on the first visit, after asking.
        private void StartOver()
        {
            Ask($"Reset everything to the way it was on your first visit?{workspace.StartOverLosses()}", "Reset everything", workspace.StartOver);
        }

        // A package file, opened and kept. A file with the name of a package that is already here replaces it, after
        // asking; one for a built in package becomes that package's changes.
        private async Task ImportPackage(InputFileChangeEventArgs e)
        {
            MachinePackage package;
            try
            {
                using var reader = new StreamReader(e.File.OpenReadStream(maxAllowedSize: 16 * 1024 * 1024));
                package = PackageFile.Read(await reader.ReadToEndAsync(), e.File.Name);
            }
            catch (Exception ex) when (ex is MachineDefinitionException || ex is IOException)
            {
                workspace.PackageError = $"Could not import {e.File.Name}: {ex.Message}";
                return;
            }
            Task Take()
            {
                workspace.Import(package);
                _ = Analytics.Import();
                return Task.CompletedTask;
            }
            if (workspace.Replaces(package))
            {
                Ask($"Replace {package.Name} with the one in {e.File.Name}? The {package.Name} you have now will be lost.", "Replace", Take);
                return;
            }
            await Take();
        }

        private async Task ExportPackage()
        {
            var package = workspace.Export();
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{package.Name}.json", package.ToJson());
            await Analytics.Export(package.Name);
        }

        // Any package here, as a file, without opening it.
        private async Task ExportByName(string name)
        {
            if (name == workspace.PackageName) { await ExportPackage(); return; }
            if (workspace.Saved(name) is not { } saved) return;
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{name}.json", saved.ToJson());
        }

        // ---- The splash screen ----

        // Opens a built in machine with one of its programs and runs it.
        private void Showcase(Showcase showcase)
        {
            splash = false;
            OpenByName(showcase.Package);
            if (workspace.Package.Programs.FirstOrDefault(p => p.Name == showcase.Program) is { } example) workspace.LoadExample(example);
            OpenTab("Run");
            autoStart = showcase.Speed;
            _ = Analytics.Splash(showcase.Package);
        }
        private void CloseSplash(string then)
        {
            splash = false;
            if (then == "guide") OpenPage(("guide", "getting-started"));
            _ = Analytics.Splash(then ?? "closed");
        }

        // ---- The editors ----

        private void OnDesignChanged(MachineDefinition definition)
        {
            if (workspace.DesignChanged(definition)) _ = Analytics.EditedHardware();
        }

        private void OnMicrocodeChanged(MicrocodeDefinition microcode)
        {
            _ = Analytics.EditedMicrocode();
            workspace.MicrocodeChanged(microcode);
        }

        private void StartMicrocode()
        {
            _ = Analytics.EditedMicrocode();
            workspace.StartMicrocode();
        }

        // Picking another program replaces the editor's text. Edits to one of your programs are already in it; edits
        // to a built in example are only in the editor, so they are not thrown away without asking.
        private void PickProgram(PackageProgram example)
        {
            if (example == workspace.CurrentProgram && workspace.IsCurrent(example)) return;
            if (workspace.HasEditsToExample)
            {
                Ask($"Throw away your changes to {workspace.CurrentProgram.Name}? To keep them, use ＋ New program and paste them in first.", "Throw away", () =>
                {
                    workspace.LoadExample(example);
                    return Task.CompletedTask;
                });
                return;
            }
            workspace.LoadExample(example);
        }
    }
}
