using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    public sealed record SharpExample(string Name, string Description, string CSharp, ProgramNeeds Needs);

    public sealed record SharpPackageProgram(string Name, string Description, string CSharp, string Assembly, ProgramNeeds Needs);

    public static class Sharp16
    {
        public const string PackageName = "SHARP-16";
        private const string ResourcePrefix = "Exuarch.Core.SharpC/";
        private const string PreviewProgram = "Sierpinski on the screen";

        private static readonly Lazy<string> machineJson = new Lazy<string>(() => Resource("sharp16-machine.json"));
        private static readonly Lazy<string> readme = new Lazy<string>(() => Resource("sharp16-readme.md"));
        private static readonly Lazy<string> description = new Lazy<string>(() => Resource("sharp16-description.txt"));
        private static readonly Lazy<IReadOnlyList<SharpExample>> examples = new Lazy<IReadOnlyList<SharpExample>>(LoadExamples);

        public static MachineDefinition Machine() => MachineDefinition.FromJson(machineJson.Value);

        public static string Readme => readme.Value;

        public static string Description => description.Value;

        public static IReadOnlyList<SharpExample> Examples => examples.Value;

        public static string ProgramSource(string csharp, string assembly)
        {
            var lines = Regex.Split(Regex.Replace(csharp ?? "", @"\s+\z", ""), "\r?\n").Select(l => (";   " + l).TrimEnd());
            var header = new[] { "; Compiled from C# by SharpC for SHARP-16", ";" }.Concat(lines).Append("");
            return string.Join("\n", header) + "\n" + Regex.Replace(assembly ?? "", @"\s+\z", "");
        }

        public static MachinePackage MakePackage(IEnumerable<SharpPackageProgram> programs)
        {
            var list = programs.ToList();
            return new MachinePackage
            {
                Name = PackageName,
                Description = Description,
                Tagline = "A target for compiled C#",
                Tags = new List<string> { "compiler", "stack", "graphics" },
                Preview = list.Any(p => p.Name == PreviewProgram) ? PreviewProgram : list.FirstOrDefault()?.Name,
                Readme = Readme,
                Machine = Machine(),
                Programs = list.Select(p => new PackageProgram { Name = p.Name, Description = p.Description, Source = ProgramSource(p.CSharp, p.Assembly), Needs = p.Needs }).ToList(),
            };
        }

        private static string Resource(string name)
        {
            using var stream = typeof(Sharp16).Assembly.GetManifestResourceStream(ResourcePrefix + name)
                ?? throw new InvalidOperationException($"The resource {name} is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static IReadOnlyList<SharpExample> LoadExamples()
        {
            using var document = JsonDocument.Parse(Resource("examples.json"));
            return document.RootElement.EnumerateArray().Select(Example).ToList();
        }

        private static SharpExample Example(JsonElement e)
        {
            var needs = e.TryGetProperty("needs", out var n) ? Needs(n) : null;
            return new SharpExample(e.GetProperty("name").GetString(), Text(e, "description"), e.GetProperty("csharp").GetString(), needs);
        }

        private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

        private static ProgramNeeds Needs(JsonElement n)
        {
            return new ProgramNeeds
            {
                Keypad = n.TryGetProperty("keypad", out var k) ? k.GetBoolean() : null,
                MinHz = n.TryGetProperty("minHz", out var min) ? min.GetInt32() : null,
                ExactHz = n.TryGetProperty("exactHz", out var exact) ? exact.GetInt32() : null,
            };
        }
    }
}
