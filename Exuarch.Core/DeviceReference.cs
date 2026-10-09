using System;
using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    // A handbook page per device type, written from what the device registry says about it, so the reference
    // always matches the devices the app has.
    public static class DeviceReference
    {
        private static readonly Lazy<DeviceRegistry> registry = new Lazy<DeviceRegistry>(() => DeviceRegistry.CreateDefault());

        // The handbook pages that explain what a device type is for, besides the general one on devices.
        private static readonly Dictionary<string, string[]> Concepts = new Dictionary<string, string[]>
        {
            ["register"] = new[] { "registers-and-the-alu" },
            ["registerFile"] = new[] { "operands", "registers-and-the-alu" },
            ["statusRegister"] = new[] { "flags-and-conditions" },
            ["dualPortRegister"] = new[] { "bridges" },
            ["instructionRegister"] = new[] { "fetch-and-the-instruction-register", "ground-zero", "fetch-routine" },
            ["clock"] = new[] { "buses-and-ticks" },
            ["alu"] = new[] { "registers-and-the-alu", "flags-and-conditions" },
            ["ram"] = new[] { "memory-and-banks" },
            ["rom"] = new[] { "memory-and-banks" },
            ["mmu"] = new[] { "memory-and-banks" },
            ["display"] = new[] { "first-machine" },
            ["framebuffer"] = new[] { "bus-masters", "graphics-pipeline" },
            ["doubleFramebuffer"] = new[] { "bus-masters", "graphics-pipeline" },
            ["blitter"] = new[] { "bus-masters", "interrupts" },
            ["interruptController"] = new[] { "taking-an-interrupt", "interrupts" },
            ["timer"] = new[] { "interrupts" },
            ["rtc"] = new[] { "interrupts" },
            ["keypad"] = new[] { "reading-the-keypad", "taking-an-interrupt", "interrupts" },
            ["rasterizer"] = new[] { "graphics-pipeline", "bus-masters", "interrupts" },
            ["depthBuffer"] = new[] { "graphics-pipeline" },
            ["mac"] = new[] { "graphics-pipeline" },
        };

        public static IEnumerable<DeviceTypeInfo> Types { get { return registry.Value.TypeInfos; } }

        // Registration order, grouped by category the way the palette groups them.
        public static IEnumerable<IGrouping<string, DeviceTypeInfo>> ByCategory { get { return Types.GroupBy(t => t.Category); } }

        // What a device type is, in a few words for a list: its description up to the first full stop, colon or comma.
        public static string Summary(DeviceTypeInfo info)
        {
            var text = info.Description ?? "";
            int end = new[] { ". ", ": ", ", " }.Select(mark => text.IndexOf(mark, StringComparison.Ordinal)).Where(i => i > 0).DefaultIfEmpty(text.Length).Min();
            return text.Substring(0, end).TrimEnd('.');
        }

        public static bool Exists(string type)
        {
            return type != null && registry.Value.IsRegistered(type);
        }

        public static string Markdown(string type)
        {
            var info = registry.Value.Info(type);
            var page = new MarkdownWriter()
                .Title(info.Type)
                .Paragraph(Sentence(info.Description))
                .Paragraph($"A device in the *{info.Category}* group of the palette. {Ports(info)}");
            foreach (var section in Sections) section(page, info);
            return page.ToString();
        }

        // The sections after the introduction, in page order. A section with nothing to list is left out.
        private static readonly Action<MarkdownWriter, DeviceTypeInfo>[] Sections = { ControlLines, Connections, Parameters, Examples, SeeAlso };

        private static string Ports(DeviceTypeInfo info)
        {
            return info.Ports.Count switch
            {
                0 => "It is on no bus.",
                1 => $"It has one bus port, `{info.Ports[0]}`.",
                _ => $"It has {info.Ports.Count} bus ports: {string.Join(", ", info.Ports.Select(p => $"`{p}`"))}.",
            };
        }

        private static void ControlLines(MarkdownWriter page, DeviceTypeInfo info)
        {
            page.Section("Control lines", "Microcode turns these on for one tick, written `<id>.<line>`.")
                .Table(new[] { "Line", "What it does", "Bus" }, info.ControlLines.Select(line => new[] { $"`{line.Name}`", MarkdownWriter.Cell(line.Description), Bus(line) }));
        }

        private static string Bus(ControlLineInfo line)
        {
            return line.Drives != null ? $"drives `{line.Drives}`" : line.Reads != null ? $"reads `{line.Reads}`" : "";
        }

        private static void Connections(MarkdownWriter page, DeviceTypeInfo info)
        {
            if (info.Connections.Count == 0) return;
            page.Section("Connections", "Wired to other devices in the hardware design, outside the buses.")
                .BulletList(info.Connections.Select(connection => $"`{connection.Name}`: {Sentence(connection.Description)}"));
        }

        private static void Parameters(MarkdownWriter page, DeviceTypeInfo info)
        {
            if (info.Parameters.Count == 0) return;
            page.Section("Parameters")
                .Table(new[] { "Parameter", "Range", "Default", "What it sets" },
                       info.Parameters.Select(parameter => new[] { $"`{parameter.Name}`", $"{parameter.Min}–{parameter.Max}", $"{parameter.Default}", MarkdownWriter.Cell(parameter.Description) }));
        }

        // The built in packages that have a device of the type, with their ids for it.
        private static void Examples(MarkdownWriter page, DeviceTypeInfo info)
        {
            var users = BuiltInPackages.Examples
                .Select(p => (Package: p, Ids: p.Machine.Devices.Where(d => d.Type == info.Type).Select(d => d.Id).ToList()))
                .Where(u => u.Ids.Count > 0)
                .ToList();
            if (users.Count == 0) return;
            page.Section("In the examples")
                .BulletList(users.Select(u => $"[{u.Package.Name}](exuarch:package/{u.Package.Name}): {string.Join(", ", u.Ids.Select(id => $"`{id}`"))}"));
        }

        private static void SeeAlso(MarkdownWriter page, DeviceTypeInfo info)
        {
            var concepts = Concepts.TryGetValue(info.Type, out var ids) ? ids : Array.Empty<string>();
            page.Section("See also")
                .BulletList(concepts.Select(id => $"[{Guides.Find(id)?.Title ?? id}](exuarch:guide/{id})").Prepend("[Devices and control lines](exuarch:guide/devices-and-control-lines)"));
        }

        private static string Sentence(string text)
        {
            text = (text ?? "").Trim();
            return text.Length == 0 || text.EndsWith(".") ? text : text + ".";
        }
    }
}
