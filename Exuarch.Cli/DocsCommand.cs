using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exuarch.Core;
namespace Exuarch.Cli
{
    public class DocsCommand : ICommand
    {
        public string Name { get { return "docs"; } }
        public string Usage { get { return "docs [<guide> | <device type>] [--search <words>]"; } }
        public string Summary { get { return "Read the handbook: list its pages, print one, or search them"; } }
        public IReadOnlyCollection<string> Options { get { return new[] { "search" }; } }

        public int Execute(Arguments arguments, TextWriter output)
        {
            arguments.NoMorePositionalThan(1);
            var search = arguments.Option("search");
            if (search != null) return Search(search, output);
            var page = arguments.OptionalPositional(0);
            if (page == null) return List(output);
            output.WriteLine(Page(page));
            return ExitCode.Success;
        }

        private static int List(TextWriter output)
        {
            foreach (var section in Guides.All.GroupBy(g => g.Section))
            {
                output.WriteLine($"{section.Key}:");
                foreach (var guide in section) output.WriteLine($"  {guide.Id,-32} {guide.Title}");
            }
            foreach (var category in DeviceReference.ByCategory)
            {
                output.WriteLine($"Devices, {category.Key}:");
                foreach (var type in category) output.WriteLine($"  {type.Type,-32} {DeviceReference.Summary(type)}");
            }
            output.WriteLine("Print a page with 'exuarch docs <name>'.");
            return ExitCode.Success;
        }

        private static string Page(string name)
        {
            var guide = Guides.Find(name) ?? Guides.All.FirstOrDefault(g => string.Equals(g.Id, name, StringComparison.OrdinalIgnoreCase));
            if (guide != null) return guide.Markdown;
            var type = DeviceReference.Types.FirstOrDefault(t => string.Equals(t.Type, name, StringComparison.OrdinalIgnoreCase));
            if (type != null) return DeviceReference.Markdown(type.Type);
            throw new UsageException($"There is no guide or device type '{name}'. Run 'exuarch docs' to list them, or 'exuarch docs --search {name}'.");
        }

        private static int Search(string words, TextWriter output)
        {
            var hits = Handbook.Search(words, null, 15);
            if (hits.Count == 0) { output.WriteLine($"Nothing in the handbook mentions '{words}'."); return ExitCode.Success; }
            foreach (var hit in hits) output.WriteLine($"{hit.Target} ({hit.Section}): {hit.Snippet}");
            return ExitCode.Success;
        }
    }
}
