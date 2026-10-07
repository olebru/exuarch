using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace Exuarch.Core
{
    // A search result: where it leads (an exuarch: link kind and target), what to show for it, and how well it matched.
    public class HandbookHit
    {
        public string Kind { get; set; }
        public string Target { get; set; }
        public string Title { get; set; }
        public string Section { get; set; }
        public string Snippet { get; set; }
        public int Score { get; set; }
    }

    // Search across the guides, the device reference and the machine that is open.
    public static class Handbook
    {
        public const string ReferenceSection = "Device reference";
        public const string MachineSection = "This machine";
        private static readonly Regex Link = new Regex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Markup = new Regex(@"[`*_#>|]+", RegexOptions.Compiled);
        private static readonly Regex Space = new Regex(@"\s+", RegexOptions.Compiled);

        private class Entry
        {
            public string Kind, Target, Title, Section, Text;
        }

        // Every word of the query has to appear in the title or the text. Titles count most, then how often the words
        // appear; pages come before the parts of the open machine when they match as well.
        public static IReadOnlyList<HandbookHit> Search(string query, MachinePackage package = null, int limit = 30)
        {
            var words = (query ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return Array.Empty<HandbookHit>();
            var phrase = string.Join(" ", words);
            var hits = new List<HandbookHit>();
            foreach (var entry in Entries(package))
            {
                var title = entry.Title.ToLowerInvariant();
                var text = entry.Text.ToLowerInvariant();
                if (!words.All(w => title.Contains(w) || text.Contains(w))) continue;
                int score = 0;
                if (title == phrase) score += 200;
                else if (title.Contains(phrase)) score += 100;
                score += words.Count(w => title.Contains(w)) * 20;
                score += words.Sum(w => Math.Min(Occurrences(text, w), 10));
                if (entry.Section == MachineSection) score -= 5;
                hits.Add(new HandbookHit
                {
                    Kind = entry.Kind, Target = entry.Target, Title = entry.Title, Section = entry.Section,
                    Snippet = Snippet(entry.Text, words), Score = score,
                });
            }
            return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        }

        // The guides and the reference do not change, so their plain text is made once.
        private static readonly Lazy<List<Entry>> pages = new Lazy<List<Entry>>(() => GuidePages().Concat(ReferencePages()).ToList());

        // What the open machine adds to the pages.
        private static readonly Func<MachinePackage, IEnumerable<Entry>>[] MachineSources = { MachineDevices, MachineInstructions };

        private static IEnumerable<Entry> Entries(MachinePackage package)
        {
            return package?.Machine == null ? pages.Value : pages.Value.Concat(MachineSources.SelectMany(source => source(package)));
        }

        private static IEnumerable<Entry> GuidePages()
        {
            return Guides.All.Select(guide => new Entry { Kind = "guide", Target = guide.Id, Title = guide.Title, Section = guide.Section, Text = Plain(Body(guide.Markdown)) });
        }

        private static IEnumerable<Entry> ReferencePages()
        {
            return DeviceReference.Types.Select(type => new Entry { Kind = "reference", Target = type.Type, Title = type.Type, Section = ReferenceSection, Text = Plain(Body(DeviceReference.Markdown(type.Type))) });
        }

        private static IEnumerable<Entry> MachineDevices(MachinePackage package)
        {
            return package.Machine.Devices.Select(device =>
            {
                var name = device.Name != null && device.Name != device.Id ? $" ({device.Name})" : "";
                return new Entry { Kind = "device", Target = device.Id, Title = device.Id, Section = MachineSection, Text = $"A {device.Type}{name} in {package.Name}." };
            });
        }

        private static IEnumerable<Entry> MachineInstructions(MachinePackage package)
        {
            return (package.Machine.Decoder?.Microcode?.AllInstructions ?? Enumerable.Empty<InstructionDefinition>()).Select(instruction =>
            {
                var signals = string.Join(" ", instruction.Steps.SelectMany(s => s.Signals).Distinct());
                return new Entry { Kind = "instruction", Target = instruction.Mnemonic, Title = instruction.Mnemonic, Section = MachineSection, Text = $"{instruction.Description} {signals}".Trim() };
            });
        }

        // The page without its title line, which is searched as the title.
        private static string Body(string markdown)
        {
            var lines = markdown.Split('\n');
            return string.Join("\n", lines.Length > 0 && lines[0].StartsWith("# ") ? lines.Skip(1) : lines);
        }

        private static string Plain(string markdown)
        {
            return Space.Replace(Markup.Replace(Link.Replace(markdown, "$1"), ""), " ").Trim();
        }

        private static int Occurrences(string text, string word)
        {
            int count = 0;
            for (int i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        // Some text around the first word that is found, cut at spaces.
        private static string Snippet(string text, string[] words)
        {
            const int before = 50, after = 110;
            var lower = text.ToLowerInvariant();
            int at = words.Select(w => lower.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
            int start = Math.Max(0, at - before);
            if (start > 0) { int space = text.IndexOf(' ', start); if (space >= 0 && space < at) start = space + 1; }
            int end = Math.Min(text.Length, at + after);
            if (end < text.Length) { int space = text.LastIndexOf(' ', end); if (space > at) end = space; }
            return (start > 0 ? "…" : "") + text.Substring(start, end - start) + (end < text.Length ? "…" : "");
        }
    }
}
