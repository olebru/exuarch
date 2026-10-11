using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace Exuarch.Core
{
    public static class ReadmeLinks
    {
        public const string Scheme = "exuarch:";
        public static readonly string[] Tabs = { "Hardware design", "Microcode", "Program", "Run" };

        private static readonly LinkKind[] LinkKinds =
        {
            new LinkKind("device", true, (package, target) => package.Machine.FindDevice(target) != null, "device"),
            new LinkKind("instruction", true, (package, target) => package.Machine.Decoder?.Microcode?.AllInstructions.Any(i => i.Mnemonic == target) == true, "instruction"),
            new LinkKind("program", true, (package, target) => package.Programs.Any(p => p.Name == target), "program"),
            new LinkKind("tab", false, (package, target) => Tabs.Contains(target), "tab"),
            new LinkKind("package", false, (package, target) => BuiltInPackages.All.Any(p => p.Name == target), "built in package"),
            new LinkKind("guide", false, (package, target) => Guides.Find(target) != null, "guide"),
            new LinkKind("reference", false, (package, target) => DeviceReference.Exists(target), "device type"),
        };
        private static readonly Dictionary<string, LinkKind> kindsByName = LinkKinds.ToDictionary(k => k.Name);

        public static readonly string[] Kinds = LinkKinds.Select(k => k.Name).ToArray();
        private static readonly Regex Markdown = new Regex(@"\]\(<?(exuarch:[^)>]+)>?\)", RegexOptions.Compiled);

        public static bool TryParse(string href, out string kind, out string target)
        {
            kind = target = null;
            if (href == null || !href.StartsWith(Scheme, StringComparison.Ordinal)) return false;
            var rest = Uri.UnescapeDataString(href.Substring(Scheme.Length));
            int slash = rest.IndexOf('/');
            if (slash <= 0) return false;
            kind = rest.Substring(0, slash);
            target = rest.Substring(slash + 1);
            return Kinds.Contains(kind) && target.Length > 0;
        }

        public static IEnumerable<string> In(string markdown)
        {
            return markdown == null ? Enumerable.Empty<string>() : Markdown.Matches(markdown).Select(m => m.Groups[1].Value);
        }

        public static string Problem(MachinePackage package, string href)
        {
            if (!TryParse(href, out var kind, out var target)) return $"'{href}' is not an exuarch: link to a device, instruction, program, tab, package, guide or reference page";
            var link = kindsByName[kind];
            if (package == null && link.NeedsMachine) return $"a guide can not link to a {kind}, only to tabs, packages, guides and the reference";
            return link.Exists(package, target) ? null : $"there is no {link.Noun} '{target}'";
        }
    }

    internal sealed record LinkKind(string Name, bool NeedsMachine, Func<MachinePackage, string, bool> Exists, string Noun);
}
