using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Core.SharpC;
using Exuarch.Web.Components;
using Exuarch.Web.Workbench;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.SharpC
{
    public partial class SharpCPage : IDisposable
    {
        private const int ToastMilliseconds = 2600;

        [Inject] private IJSRuntime JS { get; set; }

        private readonly MachineDefinition machine = Sharp16.Machine();
        private BrowserStore store;
        private SharpCWorkspace workspace;
        private SharpCGitHub gitHub;
        private SharpCompilation compilation = new SharpCompilation { Ok = true, Lines = Array.Empty<SharpAsmLine>(), Runtime = Array.Empty<string>() };
        private SharpCompilation lastGood;
        private SharpProgramSymbols symbols;
        private string assembly = "";
        private int memorySize;
        private int registerCount;
        private CSharpEditor csharpEditor;
        private DotNetObjectReference<SharpCPage> self;
        private Confirmation pendingConfirm;
        private string saveAsName;
        private bool gitHubDialog;
        private string toast;
        private int toastVersion;
        private int caretLine;
        private int hoveredSourceLine;

        private string Runtime => lastGood == null ? "" : string.Join(" ", lastGood.Runtime);
        private int SourceHighlight => hoveredSourceLine;
        private string AssemblyTitle => compilation.Ok ? "SHARP-16 assembly" : "SHARP-16 assembly · the last version that compiled";
        private int LinkedSourceLine => hoveredSourceLine > 0 ? hoveredSourceLine : caretLine;

        private IReadOnlyList<int> AssemblyHighlight
        {
            get
            {
                if (lastGood == null || LinkedSourceLine <= 0) return Array.Empty<int>();
                return lastGood.Lines.Select((line, i) => (line, i)).Where(x => x.line.SourceLine == LinkedSourceLine).Select(x => x.i + 1).ToList();
            }
        }

        protected override void OnInitialized()
        {
            store = new BrowserStore(JS);
            workspace = new SharpCWorkspace(store);
            gitHub = new SharpCGitHub(new GitHubRepo(JS));
            memorySize = machine.FindDevice(machine.ProgramMemory)?.Parameters.TryGetValue("size", out var size) == true && size.TryGetInt32(out var cells) ? cells : MemoryModule.DefaultSize;
            registerCount = RegisterFile.CountIn(machine);
            workspace.OpenProgram(SharpCWorkspace.ExampleKey(0));
            Compile();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            self = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("exuarchKeys.save", self);
            await workspace.Restore();
            await gitHub.Restore();
            Compile();
            StateHasChanged();
        }

        private void Compile()
        {
            compilation = SharpCompiler.Compile(workspace.Text);
            symbols = SharpCompiler.Symbols(workspace.Text) ?? symbols;
            if (!compilation.Ok) return;
            lastGood = compilation;
            assembly = compilation.Assembly;
        }

        private string[] Addresses(AssemblyResult result)
        {
            var byLine = result.Listing.Where(l => l.Cells.Length > 0).GroupBy(l => l.LineNumber).ToDictionary(g => g.Key, g => g.First().Address);
            int count = assembly.Split('\n').Length;
            return Enumerable.Range(1, count).Select(line => byLine.TryGetValue(line, out var address) ? address.ToString("X4") : "").ToArray();
        }

        private void HoverAssemblyLine(int line)
        {
            hoveredSourceLine = lastGood != null && line >= 1 && line <= lastGood.Lines.Count ? lastGood.Lines[line - 1].SourceLine : 0;
        }

        private async Task OnTextChanged(string text)
        {
            workspace.Edit(text);
            Compile();
            await workspace.Persist();
        }

        private async Task OpenProgram(string key)
        {
            await csharpEditor.Flush();
            workspace.OpenProgram(key);
            caretLine = 0;
            hoveredSourceLine = 0;
            Compile();
            await workspace.Persist();
        }

        [JSInvokable]
        public async Task SaveShortcut()
        {
            if (saveAsName != null || gitHubDialog) return;
            await Save();
            StateHasChanged();
        }

        private async Task Save()
        {
            await csharpEditor.Flush();
            if (!workspace.IsDirty) return;
            if (!workspace.OpenIsMine)
            {
                saveAsName = workspace.SuggestedSaveAsName;
                return;
            }
            await Saved(workspace.SaveOpen(), $"Saved {workspace.OpenName}");
        }

        private async Task StartSaveAs()
        {
            await csharpEditor.Flush();
            saveAsName = workspace.SuggestedSaveAsName;
        }

        private async Task SaveAs(string name)
        {
            saveAsName = null;
            var program = workspace.SaveAs(name);
            await Saved(program, $"Saved as {program.Name}");
        }

        private async Task Saved(SharpCProgram program, string message)
        {
            await workspace.Persist();
            await store.RequestPersistentStorage();
            ShowToast(message);
            _ = InvokeAsync(async () =>
            {
                await gitHub.Save(program);
                StateHasChanged();
            });
        }

        private async Task Discard()
        {
            await csharpEditor.Flush();
            if (!workspace.IsDirty) return;
            pendingConfirm = new Confirmation($"Discard your unsaved changes to {workspace.OpenName} and go back to the version you saved last?", "Discard changes", async () =>
            {
                workspace.Discard();
                Compile();
                await workspace.Persist();
            });
        }

        private void Delete()
        {
            if (!workspace.OpenIsMine) return;
            pendingConfirm = new Confirmation($"Delete {workspace.OpenName} from this browser? Copy it somewhere first to keep it.", "Delete", async () =>
            {
                workspace.DeleteOpen();
                Compile();
                await workspace.Persist();
            });
        }

        private async Task Confirm()
        {
            var run = pendingConfirm?.Run;
            pendingConfirm = null;
            if (run != null) await run();
        }

        private async Task Download()
        {
            await csharpEditor.Flush();
            await JS.InvokeVoidAsync("exuarchEditor.download", "SHARP-16.json", workspace.FullPackage(true).ToJson());
            ShowToast("SHARP-16.json downloaded: open it with Import… in ExµArch");
        }

        private async Task CopyAssembly()
        {
            if (!compilation.Ok)
            {
                ShowToast("Fix the error first");
                return;
            }
            bool copied = await JS.InvokeAsync<bool>("exuarchCSharp.copy", assembly);
            ShowToast(copied ? "Assembly copied" : "Copying was blocked here: select the assembly in the right pane and copy it from there");
        }

        private void GitHubDone(string message)
        {
            gitHubDialog = false;
            ShowToast(message);
        }

        private void ShowToast(string message)
        {
            toast = message;
            int version = ++toastVersion;
            _ = InvokeAsync(async () =>
            {
                await Task.Delay(ToastMilliseconds);
                if (version != toastVersion) return;
                toast = null;
                StateHasChanged();
            });
        }

        public void Dispose()
        {
            toastVersion++;
            self?.Dispose();
        }
    }
}
