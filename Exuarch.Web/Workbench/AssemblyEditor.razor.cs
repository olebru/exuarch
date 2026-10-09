using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System;
using BlazorMonaco.Editor;
using BlazorMonaco;
using Exuarch.Core;
using Exuarch.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.Workbench
{
    // Monaco editor for assembly source, with highlighting, completion, hover, problems and formatting from
    // the machine's microcode.
    public partial class AssemblyEditor
    {
        private const int DebounceMilliseconds = 250;

        [Inject] private IJSRuntime JS { get; set; }
        [Inject] private HelpService Help { get; set; }

        [Parameter] public string Value { get; set; }
        [Parameter] public EventCallback<string> ValueChanged { get; set; }
        // The instruction set the program is written for.
        [Parameter] public MicrocodeDefinition Microcode { get; set; }
        [Parameter] public int MemorySize { get; set; } = MemoryModule.DefaultSize;
        // Registers in the machine's register file, the names a register operand can take; 0 without one.
        [Parameter] public int RegisterCount { get; set; }
        [Parameter] public string EditorId { get; set; } = "asm-editor";
        [Parameter] public Func<string, AssemblyResult> Analyzer { get; set; }
        [Parameter] public Func<AssemblyResult, string[]> Margin { get; set; }

        private StandaloneCodeEditor editor;
        private DotNetObjectReference<AssemblyEditor> self;
        private AssemblyLanguage language;
        // What the language was made from; a new microcode (by reference), memory or register count makes a new one.
        private LanguageKey languageKey;
        private string registeredMnemonics;
        private string current;
        private bool ready;
        private List<AssemblyDiagnostic> diagnostics = new List<AssemblyDiagnostic>();
        private int ErrorCount => DiagnosticStyle.Errors(diagnostics);
        private int WarningCount => DiagnosticStyle.Warnings(diagnostics);
        private int cellCount;
        private AssemblyResult lastResult = new AssemblyResult();
        private CancellationTokenSource pending;
        // The Value the page last gave or was given: the page echoing it back is not a change to show.
        private string lastValue;

        private string monacoTheme = "exuarch-dark";
        protected override async Task OnInitializedAsync()
        {
            monacoTheme = await JS.InvokeAsync<string>("exuarchTheme.monaco");
        }

        private StandaloneEditorConstructionOptions Options(StandaloneCodeEditor _)
        {
            return new StandaloneEditorConstructionOptions
            {
                Language = "exuarch-asm",
                // Monaco's own theme of the right brightness until ours is defined, when the language registers.
                Theme = monacoTheme == "exuarch" ? "vs" : "vs-dark",
                Value = Value ?? "",
                AutomaticLayout = true,
                FormatOnType = true,
                FormatOnPaste = true,
                FontSize = 13,
                FontFamily = "'IBM Plex Mono', ui-monospace, Menlo, monospace",
                LineHeight = 22,
                Padding = new EditorPaddingOptions { Top = 10 },
                TabSize = 8,
                InsertSpaces = true,
                ScrollBeyondLastLine = false,
                Minimap = new EditorMinimapOptions { Enabled = false },
                LineNumbersMinChars = Margin != null ? 7 : 3,
                RenderLineHighlight = "all",
                FixedOverflowWidgets = true,
            };
        }

        private sealed record LanguageKey(MicrocodeDefinition Microcode, int MemorySize, int RegisterCount);

        protected override async Task OnParametersSetAsync()
        {
            await UpdateLanguage();
            await SyncValue();
        }

        private async Task UpdateLanguage()
        {
            var key = new LanguageKey(Microcode, MemorySize, RegisterCount);
            if (key == languageKey) return;
            languageKey = key;
            language = new AssemblyLanguage(Microcode, MemorySize, RegisterCount);
            if (ready) await RegisterLanguage();
            Analyze(current ?? Value ?? "");
        }

        // Only a value the page did not get from this editor replaces the text: while the editor waits to send what
        // was typed, the page still holds the older text, and putting that back would lose the last keystrokes and
        // send the cursor to the start.
        private async Task SyncValue()
        {
            if (!ready || Value == lastValue) return;
            lastValue = Value;
            if (Value == current) return;
            current = Value;
            await editor.SetValue(Value ?? "");
        }

        private async Task OnInit()
        {
            self = DotNetObjectReference.Create(this);
            ready = true;
            current = Value ?? "";
            lastValue = Value;
            await RegisterLanguage();
            await JS.InvokeVoidAsync("exuarchAsm.attach", EditorId, self, Margin != null);
            await Global.SetModelLanguage(JS, await editor.GetModel(), "exuarch-asm");
            await Global.SetTheme(JS, await JS.InvokeAsync<string>("exuarchTheme.monaco"));
            Analyze(current);
            await PushMarkers();
            StateHasChanged();
        }

        private async Task RegisterLanguage()
        {
            var mnemonics = language.Instructions.Select(i => i.Mnemonic).ToArray();
            var key = string.Join(",", mnemonics);
            if (key == registeredMnemonics) return;
            registeredMnemonics = key;
            await JS.InvokeVoidAsync("exuarchAsm.register", self, mnemonics);
        }

        private async Task OnChanged(ModelContentChangedEvent _)
        {
            current = await editor.GetValue();
            pending?.Cancel();
            var token = (pending = new CancellationTokenSource()).Token;
            try { await Task.Delay(DebounceMilliseconds, token); }
            catch (TaskCanceledException) { return; }
            Analyze(current);
            await PushMarkers();
            lastValue = current;
            await ValueChanged.InvokeAsync(current);
            StateHasChanged();
        }

        private void Analyze(string source)
        {
            var result = Analyzer?.Invoke(source) ?? language.Analyze(source);
            lastResult = result;
            diagnostics = result.Diagnostics;
            cellCount = result.Cells.Length;
        }

        private async Task PushMarkers()
        {
            if (!ready) return;
            var markers = diagnostics.Select(d => new Marker { Line = d.Line, StartColumn = d.StartColumn, EndColumn = d.EndColumn, Message = d.Message, Warning = d.Severity == DiagnosticSeverity.Warning }).ToList();
            await JS.InvokeVoidAsync("exuarchAsm.setMarkers", EditorId, JsonSerializer.Serialize(markers, EditorJsonContext.Default.ListMarker));
            if (Margin != null) await JS.InvokeVoidAsync("exuarchAsm.setMargin", EditorId, Margin(lastResult));
        }

        private async Task Format()
        {
            await JS.InvokeVoidAsync("exuarchAsm.format", EditorId);
        }

        private async Task Reveal(AssemblyDiagnostic diagnostic)
        {
            await editor.SetPosition(new BlazorMonaco.Position { LineNumber = diagnostic.Line, Column = diagnostic.StartColumn }, "problem");
            await editor.RevealLineInCenter(diagnostic.Line);
            await editor.Focus();
        }

        // ---- Called from asmLanguage.js ----

        [JSInvokable]
        public string Complete(string source, int line, int column)
        {
            var items = language.Complete(source, line, column).Select(i => new Suggestion
            {
                Label = i.Label,
                Kind = (int)i.Kind,
                Detail = i.Detail,
                Documentation = i.Documentation,
                InsertText = i.InsertText,
            }).ToList();
            return JsonSerializer.Serialize(items, EditorJsonContext.Default.ListSuggestion);
        }

        [JSInvokable]
        public string Hover(string source, int line, int column)
        {
            return language.Hover(source, line, column);
        }

        // A handbook link in a hover.
        [JSInvokable]
        public void OpenLink(string href)
        {
            if (ReadmeLinks.TryParse(href, out var kind, out var target)) Help.Open(kind, target);
        }

        [JSInvokable]
        public string Format(string source)
        {
            return language.FormatDocument(source);
        }

        [JSInvokable]
        public string FormatLine(string source, int line)
        {
            return language.FormatDocumentLine(source, line);
        }

        public void Dispose()
        {
            pending?.Cancel();
            self?.Dispose();
        }
    }

    public class Suggestion
    {
        public string Label { get; set; }
        public int Kind { get; set; }
        public string Detail { get; set; }
        public string Documentation { get; set; }
        public string InsertText { get; set; }
    }

    public class Marker
    {
        public int Line { get; set; }
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
        public string Message { get; set; }
        public bool Warning { get; set; }
    }

    // Source generated so the shapes survive trimming in Release builds.
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(List<Suggestion>))]
    [JsonSerializable(typeof(List<Marker>))]
    internal partial class EditorJsonContext : JsonSerializerContext
    {
    }
}
