using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Exuarch.Core
{
    // A handbook page about using the app, as opposed to a package README about one machine.
    public class Guide
    {
        // The file name without its number prefix: Guides/concepts/03-microcode.md is "microcode".
        public string Id { get; set; }
        public string Title { get; set; }
        // Start, Tutorials or Concepts, from the folder the page is in.
        public string Section { get; set; }
        public string Markdown { get; set; }
    }

    // The guides embedded from Guides/<section>/NN-<id>.md, in section order and then file name order; the title is
    // the first "# " heading.
    public static class Guides
    {
        private const string Prefix = "Exuarch.Core.Guides/";
        public static readonly (string Folder, string Title)[] Sections = { ("start", "Start"), ("tutorials", "Tutorials"), ("concepts", "Concepts") };
        // The repository's CHANGELOG.md, embedded as a Start page. It changes with every release.
        public const string ChangelogId = "changelog";
        private static readonly Lazy<IReadOnlyList<Guide>> all = new Lazy<IReadOnlyList<Guide>>(Load);

        public static IReadOnlyList<Guide> All { get { return all.Value; } }

        public static Guide Find(string id)
        {
            return All.FirstOrDefault(g => g.Id == id);
        }

        private static IReadOnlyList<Guide> Load()
        {
            var assembly = typeof(Guides).Assembly;
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix) && n.EndsWith(".md")).ToList();
            return Sections.SelectMany(section => names
                .Where(n => n.StartsWith($"{Prefix}{section.Folder}/"))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n =>
                {
                    using var reader = new StreamReader(assembly.GetManifestResourceStream(n));
                    var markdown = reader.ReadToEnd().TrimEnd('\n', '\r');
                    var file = Path.GetFileNameWithoutExtension(n.Substring(n.LastIndexOf('/') + 1));
                    int dash = file.IndexOf('-');
                    var id = dash > 0 && file.Take(dash).All(char.IsDigit) ? file.Substring(dash + 1) : file;
                    var heading = markdown.Split('\n').FirstOrDefault(l => l.StartsWith("# "));
                    return new Guide { Id = id, Title = heading?.Substring(2).Trim() ?? id, Section = section.Title, Markdown = markdown };
                }))
                .ToList();
        }
    }
}
