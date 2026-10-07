using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace Exuarch.Core
{
    // Links in a package README or a guide that point into the app, written exuarch:<kind>/<target>:
    //   exuarch:device/alu              the device, in the hardware design
    //   exuarch:instruction/ADD         the instruction, in the microcode editor
    //   exuarch:program/Hello, world    that example program (write the link as <exuarch:program/...> when it has spaces)
    //   exuarch:tab/Run                 one of the app's tabs
    //   exuarch:package/RISC-16         a built in package, loaded in place of the current machine
    //   exuarch:guide/microcode         a handbook page
    //   exuarch:reference/alu           the reference page of a device type
    // Guides are not about one machine, so they can only link to tabs, packages, guides and the reference.
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

        // Every exuarch: link in a README, as written.
        public static IEnumerable<string> In(string markdown)
        {
            return markdown == null ? Enumerable.Empty<string>() : Markdown.Matches(markdown).Select(m => m.Groups[1].Value);
        }

        // Why the link does not lead anywhere in this package, or null when it does. Without a package (in a guide)
        // only the machine free kinds work.
        public static string Problem(MachinePackage package, string href)
        {
            if (!TryParse(href, out var kind, out var target)) return $"'{href}' is not an exuarch: link to a device, instruction, program, tab, package, guide or reference page";
            var link = kindsByName[kind];
            if (package == null && link.NeedsMachine) return $"a guide can not link to a {kind}, only to tabs, packages, guides and the reference";
            return link.Exists(package, target) ? null : $"there is no {link.Noun} '{target}'";
        }
    }

    // A kind of link: whether it needs a machine to lead somewhere, whether a target exists, and what a target is
    // called when it does not.
    internal sealed record LinkKind(string Name, bool NeedsMachine, Func<MachinePackage, string, bool> Exists, string Noun);
}
