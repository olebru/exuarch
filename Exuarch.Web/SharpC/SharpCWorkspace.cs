using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Core.SharpC;
using Exuarch.Web.Components;

namespace Exuarch.Web.SharpC
{
    public sealed class SharpCProgram
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; }
    }

    public sealed class LegacySharpCDraft
    {
        [JsonPropertyName("text")] public string Text { get; set; }
        [JsonPropertyName("example")] public string Example { get; set; }
    }

    [JsonSerializable(typeof(List<SharpCProgram>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(LegacySharpCDraft))]
    [JsonSerializable(typeof(string))]
    internal partial class SharpCJsonContext : JsonSerializerContext
    {
    }

    public sealed class SharpCWorkspace
    {
        public const string ProgramsKey = "sharpc-programs";
        public const string DraftsKey = "sharpc-drafts";
        public const string OpenKey = "sharpc-open";
        public const string LegacyDraftKey = "sharpc-draft";
        private const string ExamplePrefix = "example:";
        private const string MinePrefix = "mine:";
        private const string OwnDescription = "Compiled from C# in SharpC";

        private readonly IBrowserStore store;

        public SharpCWorkspace(IBrowserStore store)
        {
            this.store = store;
        }

        public List<SharpCProgram> Saved { get; private set; } = new List<SharpCProgram>();
        public Dictionary<string, string> Drafts { get; private set; } = new Dictionary<string, string>();
        public string Open { get; private set; } = ExampleKey(0);
        public string Text { get; private set; } = "";
        public string StorageWarning { get; set; }
        public string RecoveredNote { get; set; }

        public static string ExampleKey(int index) => $"{ExamplePrefix}{index}";
        public static string MineKey(string name) => $"{MinePrefix}{name}";
        public static bool IsMine(string key) => key.StartsWith(MinePrefix, StringComparison.Ordinal);

        public bool IsDirty => Drafts.ContainsKey(Open);
        public bool OpenIsMine => IsMine(Open);
        public string OpenName => NameOf(Open);
        public IEnumerable<(string Key, string Name)> Examples => Sharp16.Examples.Select((e, i) => (ExampleKey(i), e.Name));
        public IEnumerable<(string Key, string Name)> Mine => Saved.Select(p => (MineKey(p.Name), p.Name));

        public string NameOf(string key) => IsMine(key) ? key.Substring(MinePrefix.Length) : ExampleAt(key)?.Name;

        public string SavedText(string key) => IsMine(key) ? Saved.FirstOrDefault(p => MineKey(p.Name) == key)?.Text : ExampleAt(key)?.CSharp;

        private static SharpExample ExampleAt(string key)
        {
            if (!key.StartsWith(ExamplePrefix, StringComparison.Ordinal) || !int.TryParse(key.Substring(ExamplePrefix.Length), out var index)) return null;
            return index >= 0 && index < Sharp16.Examples.Count ? Sharp16.Examples[index] : null;
        }

        public async Task Restore()
        {
            await ReadStored();
            await TakeLegacyDraft();
            foreach (var key in Drafts.Keys.Where(k => SavedText(k) == null || Drafts[k] == null).ToList()) Drafts.Remove(key);
            OpenProgram(Open);
            if (IsDirty) RecoveredNote = $"{OpenName} has changes from your last visit that were never saved. Save keeps them; Discard changes goes back to the saved version.";
            await Persist();
        }

        private async Task ReadStored()
        {
            var saved = Read(await store.Get(ProgramsKey), SharpCJsonContext.Default.ListSharpCProgram) ?? new List<SharpCProgram>();
            Saved = saved.Where(p => p?.Name != null && p.Text != null).ToList();
            Drafts = Read(await store.Get(DraftsKey), SharpCJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>();
            Open = Read(await store.Get(OpenKey), SharpCJsonContext.Default.String) ?? ExampleKey(0);
        }

        private async Task TakeLegacyDraft()
        {
            var legacy = Read(await store.Get(LegacyDraftKey), SharpCJsonContext.Default.LegacySharpCDraft);
            if (legacy?.Text == null || !int.TryParse(legacy.Example, out var index) || ExampleAt(ExampleKey(index)) == null) return;
            Open = ExampleKey(index);
            if (legacy.Text != SavedText(Open)) Drafts[Open] = legacy.Text;
            await store.Keep(LegacyDraftKey, "null");
        }

        private static T Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize(json, type); }
            catch (JsonException) { return null; }
        }

        public async Task Persist()
        {
            bool ok = await store.Set(ProgramsKey, JsonSerializer.Serialize(Saved, SharpCJsonContext.Default.ListSharpCProgram))
                && await store.Set(DraftsKey, JsonSerializer.Serialize(Drafts, SharpCJsonContext.Default.DictionaryStringString))
                && await store.Set(OpenKey, JsonSerializer.Serialize(Open, SharpCJsonContext.Default.String));
            StorageWarning = ok ? null : "This browser's storage is off or full: copy your program somewhere safe.";
        }

        public void OpenProgram(string key)
        {
            Open = SavedText(key) == null ? ExampleKey(0) : key;
            Text = Drafts.TryGetValue(Open, out var draft) ? draft : SavedText(Open);
            RecoveredNote = null;
        }

        public void Edit(string text)
        {
            Text = text ?? "";
            if (Text == SavedText(Open)) Drafts.Remove(Open);
            else Drafts[Open] = Text;
        }

        public SharpCProgram SaveOpen()
        {
            var program = Saved.FirstOrDefault(p => MineKey(p.Name) == Open);
            if (program == null) return null;
            program.Text = Text;
            Drafts.Remove(Open);
            RecoveredNote = null;
            return program;
        }

        public SharpCProgram SaveAs(string name)
        {
            var program = new SharpCProgram { Name = UniqueName(string.IsNullOrWhiteSpace(name) ? "My program" : name.Trim()), Text = Text };
            Drafts.Remove(Open);
            Saved.Add(program);
            Open = MineKey(program.Name);
            RecoveredNote = null;
            return program;
        }

        public void Discard()
        {
            Drafts.Remove(Open);
            OpenProgram(Open);
        }

        public void DeleteOpen()
        {
            if (!OpenIsMine) return;
            Saved.RemoveAll(p => MineKey(p.Name) == Open);
            Drafts.Remove(Open);
            OpenProgram(ExampleKey(0));
        }

        public string SuggestedSaveAsName => OpenIsMine ? $"{OpenName} copy" : $"My {OpenName}";

        public string UniqueName(string name)
        {
            var unique = name;
            for (int n = 2; Saved.Any(p => p.Name == unique); n++) unique = $"{name} {n}";
            return unique;
        }

        public static SharpPackageProgram PackageProgram(string name, string csharp, SharpExample example = null)
        {
            var compiled = SharpCompiler.Compile(csharp);
            if (!compiled.Ok) return null;
            var needs = example != null ? example.Needs : csharp.Contains("Keys.Read") ? new ProgramNeeds { Keypad = true, MinHz = 50000 } : null;
            return new SharpPackageProgram(name, example?.Description ?? OwnDescription, csharp, compiled.Assembly, needs);
        }

        public MachinePackage FullPackage(bool withUnsavedChanges)
        {
            string TextOf(string key, string savedText) => withUnsavedChanges && Drafts.TryGetValue(key, out var draft) ? draft : savedText;
            var own = Saved.Select(p => PackageProgram(p.Name, TextOf(MineKey(p.Name), p.Text))).ToList();
            own.AddRange(Sharp16.Examples.Select((e, i) => (Example: e, Text: TextOf(ExampleKey(i), e.CSharp)))
                .Where(x => x.Text != x.Example.CSharp)
                .Select(x => PackageProgram(UniqueName($"My {x.Example.Name}"), x.Text)));
            var programs = own.Where(p => p != null).ToList();
            programs.AddRange(Sharp16.Examples.Where(e => programs.All(p => p.Name != e.Name)).Select(e => PackageProgram(e.Name, e.CSharp, e)).Where(p => p != null));
            return Sharp16.MakePackage(programs);
        }
    }
}
