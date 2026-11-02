using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlazorMonaco.Editor;
using Exuarch.Core.SharpC;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Exuarch.Web.SharpC
{
    public partial class CSharpEditor
    {
        private const int DebounceMilliseconds = 150;

        [Inject] private IJSRuntime JS { get; set; }

        [Parameter] public string Value { get; set; }
        [Parameter] public EventCallback<string> ValueChanged { get; set; }
        [Parameter] public SharpCompileError Error { get; set; }
        [Parameter] public string Runtime { get; set; }
        [Parameter] public Func<SharpProgramSymbols> Symbols { get; set; }
        [Parameter] public int HighlightLine { get; set; }
        [Parameter] public EventCallback<int> OnCaretLine { get; set; }
        [Parameter] public string EditorId { get; set; } = "csharp-editor";

        private StandaloneCodeEditor editor;
        private DotNetObjectReference<CSharpEditor> self;
        private CancellationTokenSource pending;
        private string current;
        private string lastValue;
        private bool ready;
        private SharpCompileError shownError;
        private int shownHighlight = -1;

        private string Hint => string.IsNullOrEmpty(Runtime) ? "Ctrl+Space suggestions · hover for details" : $"runtime: {Runtime}";

        private StandaloneEditorConstructionOptions Options(StandaloneCodeEditor _)
        {
            return new StandaloneEditorConstructionOptions
            {
                Language = "csharp",
                Value = Value ?? "",
                AutomaticLayout = true,
                FormatOnType = true,
                FormatOnPaste = true,
                FontSize = 13,
                FontFamily = "'IBM Plex Mono', ui-monospace, Menlo, monospace",
                LineHeight = 22,
                Padding = new EditorPaddingOptions { Top = 10 },
                TabSize = 4,
                InsertSpaces = true,
                ScrollBeyondLastLine = false,
                Minimap = new EditorMinimapOptions { Enabled = false },
                LineNumbersMinChars = 3,
                RenderLineHighlight = "all",
                FixedOverflowWidgets = true,
                SuggestOnTriggerCharacters = true,
            };
        }

        private async Task OnInit()
        {
            self = DotNetObjectReference.Create(this);
            current = Value ?? "";
            lastValue = Value;
            ready = true;
            await JS.InvokeVoidAsync("exuarchCSharp.attach", EditorId, self);
            await Global.SetTheme(JS, await JS.InvokeAsync<string>("exuarchTheme.monaco"));
            await PushError();
            await PushHighlight();
        }

        protected override async Task OnParametersSetAsync()
        {
            await SyncValue();
            await PushError();
            await PushHighlight();
        }

        private async Task SyncValue()
        {
            if (!ready || Value == lastValue) return;
            lastValue = Value;
            if (Value == current) return;
            current = Value ?? "";
            pending?.Cancel();
            await editor.SetValue(current);
        }

        private async Task OnChanged(ModelContentChangedEvent _)
        {
            current = await editor.GetValue();
            pending?.Cancel();
            var token = (pending = new CancellationTokenSource()).Token;
            try { await Task.Delay(DebounceMilliseconds, token); }
            catch (TaskCanceledException) { return; }
            pending = null;
            await Report();
        }

        public async Task Flush()
        {
            var waiting = pending;
            if (waiting == null) return;
            pending = null;
            waiting.Cancel();
            await Report();
        }

        private async Task Report()
        {
            lastValue = current;
            await ValueChanged.InvokeAsync(current);
        }

        private async Task PushError()
        {
            if (!ready || Equals(Error, shownError)) return;
            shownError = Error;
            var markers = Error == null ? new List<CSharpMarker>() : new List<CSharpMarker> { MarkerFor(Error) };
            await JS.InvokeVoidAsync("exuarchCSharp.setMarkers", EditorId, JsonSerializer.Serialize(markers, CSharpEditorJsonContext.Default.ListCSharpMarker));
        }

        private CSharpMarker MarkerFor(SharpCompileError error)
        {
            var lines = (current ?? "").Replace("\r\n", "\n").Split('\n');
            var text = error.Line >= 1 && error.Line <= lines.Length ? lines[error.Line - 1] : "";
            int start = Math.Max(1, error.Col);
            int end = start;
            while (end - 1 < text.Length && (char.IsLetterOrDigit(text[end - 1]) || text[end - 1] == '_')) end++;
            return new CSharpMarker { Line = Math.Max(1, error.Line), StartColumn = start, EndColumn = Math.Max(end, start + 1), Message = error.Message };
        }

        private async Task PushHighlight()
        {
            if (!ready || HighlightLine == shownHighlight) return;
            shownHighlight = HighlightLine;
            await JS.InvokeVoidAsync("exuarchLines.highlight", EditorId, HighlightLine > 0 ? new[] { HighlightLine } : Array.Empty<int>(), false);
        }

        private async Task Format()
        {
            await JS.InvokeVoidAsync("exuarchCSharp.format", EditorId);
        }

        private async Task RevealError()
        {
            if (Error == null) return;
            await editor.SetPosition(new BlazorMonaco.Position { LineNumber = Math.Max(1, Error.Line), Column = Math.Max(1, Error.Col) }, "problem");
            await editor.RevealLineInCenter(Math.Max(1, Error.Line));
            await editor.Focus();
        }

        [JSInvokable]
        public Task CaretLine(int line) => OnCaretLine.InvokeAsync(line);

        [JSInvokable]
        public string Complete(string source, int line, int column)
        {
            var items = SharpLanguageService.Complete(source, line, column, Symbols?.Invoke()).Select(i => new CSharpSuggestion
            {
                Label = i.Label,
                Kind = i.Kind.ToString(),
                Detail = i.Detail,
                Documentation = i.Documentation,
                InsertText = i.InsertText,
                IsSnippet = i.IsSnippet,
            }).ToList();
            return JsonSerializer.Serialize(items, CSharpEditorJsonContext.Default.ListCSharpSuggestion);
        }

        [JSInvokable]
        public string Hover(string source, int line, int column) => SharpLanguageService.Hover(source, line, column, Symbols?.Invoke());

        [JSInvokable]
        public string SignatureHelp(string source, int line, int column)
        {
            var help = SharpLanguageService.SignatureHelp(source, line, column, Symbols?.Invoke());
            if (help == null) return null;
            var dto = new CSharpSignatureHelp
            {
                ActiveParameter = help.ActiveParameter,
                Signatures = help.Signatures.Select(s => new CSharpSignature { Label = s.Label, Documentation = s.Documentation, Parameters = s.Parameters.ToList() }).ToList(),
            };
            return JsonSerializer.Serialize(dto, CSharpEditorJsonContext.Default.CSharpSignatureHelp);
        }

        [JSInvokable]
        public string Format(string source, int line, int column) => SharpFormatter.Format(source);

        [JSInvokable]
        public string FormatLine(string source, int line, int column) => SharpFormatter.FormatLine(source, line);

        public void Dispose()
        {
            pending?.Cancel();
            self?.Dispose();
        }
    }

    public class CSharpSuggestion
    {
        public string Label { get; set; }
        public string Kind { get; set; }
        public string Detail { get; set; }
        public string Documentation { get; set; }
        public string InsertText { get; set; }
        public bool IsSnippet { get; set; }
    }

    public class CSharpSignature
    {
        public string Label { get; set; }
        public string Documentation { get; set; }
        public List<string> Parameters { get; set; }
    }

    public class CSharpSignatureHelp
    {
        public List<CSharpSignature> Signatures { get; set; }
        public int ActiveParameter { get; set; }
    }

    public class CSharpMarker
    {
        public int Line { get; set; }
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
        public string Message { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(List<CSharpSuggestion>))]
    [JsonSerializable(typeof(CSharpSignatureHelp))]
    [JsonSerializable(typeof(List<CSharpMarker>))]
    internal partial class CSharpEditorJsonContext : JsonSerializerContext
    {
    }
}
