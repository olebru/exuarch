using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Components;
using Exuarch.Web.Drawer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Exuarch.Web.Workbench
{
    public partial class ComputerSIM : IDisposable
    {
        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private HelpService Help { get; set; }
        [Inject] private Analytics Analytics { get; set; }
        [Inject] private DeviceRegistry Registry { get; set; }
        [Inject] private CommunityCatalog Community { get; set; }

        private WorkspaceController workspace;
        private WorkspaceTab[] tabs;
        private WorkspaceTab activeTab;
        private LinkRouter links;
        private bool drawerOpen;
        private string drawerSection = "Handbook";
        private (string Kind, string Target)? drawerPage;
        private Confirmation pendingConfirm;
        private bool newDialog;
        private bool gallery;
        private readonly MachineBrowser galleryBrowser = new MachineBrowser();
        private PackageActions galleryActions;
        private readonly NewMachineForm newMachine = new NewMachineForm();
        private bool splash;
        private string autoStart;
        private string focusDevice;
        private string focusMnemonic;
        private int focusStep;
        private int focusVersion;
        private bool disposed;
        private const int HeartbeatMilliseconds = 5000;
        private const double ActiveWithinIdleSeconds = 60;

        protected override void OnInitialized()
        {
            workspace = new WorkspaceController(new BrowserStore(JS), Registry);
            workspace.Changed += () => InvokeAsync(StateHasChanged);
            tabs = new[]
            {
                new WorkspaceTab("Hardware design", () => new TabBadge(workspace.Validation.DefinitionErrors.Count, "danger"), () => HardwareBody),
                new WorkspaceTab("Microcode", MicrocodeBadge, () => MicrocodeBody),
                new WorkspaceTab("Program", () => new TabBadge(workspace.Validation.ProgramErrors.Count, "danger"), () => ProgramBody),
                new WorkspaceTab("Run", RunBadge, () => RunBody),
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

        private TabBadge MicrocodeBadge()
        {
            var validation = workspace.Validation;
            return validation.MicrocodeErrorCount > 0 ? new TabBadge(validation.MicrocodeErrorCount, "danger") : new TabBadge(validation.MicrocodeWarningCount, "warning");
        }

        private TabBadge RunBadge()
        {
            var validation = workspace.Validation;
            return validation.Machine == null ? new TabBadge(validation.AllErrors.Count(), "danger") : TabBadge.None;
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

        private async Task Heartbeat()
        {
            while (!disposed)
            {
                await Task.Delay(HeartbeatMilliseconds);
                if (!disposed) await ObserveView();
            }
        }
        private async Task ObserveView()
        {
            bool active = await Analytics.IdleSeconds() < ActiveWithinIdleSeconds;
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

        private void ShowConfirmation(string message, string action, Func<Task> run)
        {
            pendingConfirm = new Confirmation(message, action, run);
        }
        private async Task Confirm()
        {
            var run = pendingConfirm?.Run;
            pendingConfirm = null;
            if (run != null) await run();
        }

        private void ToggleDrawer(string section)
        {
            drawerOpen = !drawerOpen || drawerSection != section;
            drawerSection = section;
        }

        private void OnHelp(string kind, string target)
        {
            InvokeAsync(() =>
            {
                FollowLink((kind, target));
                StateHasChanged();
            });
        }

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

        private void OpenThisMachine()
        {
            drawerOpen = true;
            drawerSection = "This machine";
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

        private PackageActions GalleryActions => galleryActions ??= new PackageActions(
            EventCallback.Factory.Create(this, () => { gallery = false; drawerOpen = true; drawerSection = "This machine"; }),
            EventCallback.Factory.Create<string>(this, name => { gallery = false; OpenByName(name); }),
            EventCallback.Factory.Create<string>(this, ExportByName),
            EventCallback.Factory.Create<string>(this, DeletePackage),
            EventCallback.Factory.Create<string>(this, ResetPackage));

        private void OpenGallery() => ShowGallery(community: false);

        private void OpenCommunity() => ShowGallery(community: true);

        private void ShowGallery(bool community)
        {
            galleryBrowser.ShowsCommunity = community;
            galleryBrowser.Toggle(null);
            gallery = true;
        }

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

        private void ResetPackage(string name)
        {
            if (!workspace.CanReset(name)) return;
            ShowConfirmation($"Revert {name} to the way it ships? Your changes to it will be lost; Export first to keep them.", "Revert", () => workspace.Reset(name));
        }

        private void DeletePackage(string name)
        {
            if (Workspace.IsBuiltIn(name)) return;
            ShowConfirmation($"Delete {name} from this browser? Export it first to keep a copy.", "Delete", () => workspace.Delete(name));
        }

        private void StartOver()
        {
            ShowConfirmation($"Reset everything to the way it was on your first visit?{workspace.StartOverLosses()}", "Reset everything", workspace.StartOver);
        }

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
            await Take(package, e.File.Name);
        }

        private async Task Take(MachinePackage package, string source)
        {
            Task Keep()
            {
                workspace.Import(package);
                _ = Analytics.Import();
                return Task.CompletedTask;
            }
            if (workspace.Replaces(package))
            {
                ShowConfirmation($"Replace {package.Name} with the one in {source}? The {package.Name} you have now will be lost.", "Replace", Keep);
                return;
            }
            await Keep();
        }

        private async Task AddCommunityMachine(CommunityMachine machine)
        {
            MachinePackage package;
            try
            {
                package = await Community.FetchAsync(machine);
            }
            catch (Exception ex) when (ex is MachineDefinitionException || ex is System.Net.Http.HttpRequestException || ex is ArgumentException || ex is TaskCanceledException)
            {
                workspace.PackageError = $"Could not add {machine.Name} from the community collection: {ex.Message}";
                return;
            }
            gallery = false;
            await Take(package, "the community collection");
        }

        private async Task ExportPackage()
        {
            var package = workspace.Export();
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{package.Name}.json", package.ToJson());
            await Analytics.Export(package.Name);
        }

        private async Task ExportByName(string name)
        {
            if (name == workspace.PackageName) { await ExportPackage(); return; }
            if (workspace.Saved(name) is not { } saved) return;
            await JS.InvokeVoidAsync("exuarchEditor.download", $"{name}.json", saved.ToJson());
        }

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

        private void PickProgram(PackageProgram example)
        {
            if (example == workspace.CurrentProgram && workspace.IsCurrent(example)) return;
            if (workspace.HasEditsToExample)
            {
                ShowConfirmation($"Throw away your changes to {workspace.CurrentProgram.Name}? To keep them, use ＋ New program and paste them in first.", "Throw away", () =>
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
