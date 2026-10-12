using System;
using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Drawer
{
    public sealed record PackageActions(EventCallback Read, EventCallback<string> Load, EventCallback<string> Export, EventCallback<string> Delete, EventCallback<string> Reset);

    public sealed record MachineGroup(string Title, IReadOnlyList<MachinePackage> Machines);

    public sealed class MachineBrowser
    {
        public const string All = "All";
        public const string Yours = "Yours";
        public const string Graphics = "Graphics";
        public const string Open = "Open";
        public static readonly string[] Filters = { All, Yours, "Simple", "Advanced", "Ludicrous", Graphics };
        private static readonly string[] ExampleLevels = BuiltInPackages.Levels.Where(l => l != BuiltInPackages.TutorialLevel).ToArray();

        public string Query { get; set; } = "";
        public string Filter { get; set; } = All;
        public string Expanded { get; private set; }

        public void Toggle(string name)
        {
            Expanded = Expanded == name ? null : name;
        }

        public void SelectFilter(string filter)
        {
            Filter = filter;
        }

        private readonly Dictionary<string, bool> groupChoices = new Dictionary<string, bool>();
        private string groupChoicesFor;

        public bool CollapsesGroups => Filter == All && string.IsNullOrWhiteSpace(Query);

        public bool GroupOpen(MachineGroup group, string current)
        {
            if (!CollapsesGroups) return true;
            if (groupChoicesFor != current)
            {
                groupChoices.Clear();
                groupChoicesFor = current;
            }
            return groupChoices.TryGetValue(group.Title, out var open) ? open : group.Machines.Any(m => m.Name == current);
        }

        public void ToggleGroup(MachineGroup group, string current)
        {
            groupChoices[group.Title] = !GroupOpen(group, current);
        }

        public bool ShowsCommunity { get; set; }

        public IReadOnlyList<CommunityMachine> CommunityShown(IEnumerable<CommunityMachine> machines)
        {
            return ShowsCommunity ? machines.Where(m => m.Matches(Query)).ToList() : new List<CommunityMachine>();
        }

        public IReadOnlyList<MachineGroup> Groups(IEnumerable<MachinePackage> own, MachinePackage current)
        {
            var groups = new List<MachineGroup>();
            if (ShowsCommunity) return groups;
            if (ShowsAsOpen(current)) groups.Add(new MachineGroup(Open, new[] { current }));
            Add(groups, Yours, own.Where(p => Filter is All or Yours or Graphics));
            foreach (var level in ExampleLevels)
            {
                var title = char.ToUpperInvariant(level[0]) + level.Substring(1);
                var examples = BuiltInPackages.Examples.Where(p => BuiltInPackages.Level(p.Name) == level);
                Add(groups, title, examples.Where(p => Filter is All or Graphics || Filter == title));
            }
            return groups;
        }

        private bool ShowsAsOpen(MachinePackage current)
        {
            return current != null && Filter == All && BuiltInPackages.Level(current.Name) == BuiltInPackages.TutorialLevel && current.Matches(Query);
        }

        private void Add(List<MachineGroup> groups, string title, IEnumerable<MachinePackage> machines)
        {
            var shown = machines.Where(p => p.Matches(Query) && (Filter != Graphics || p.Tags?.Contains("graphics") == true)).ToList();
            if (shown.Count > 0) groups.Add(new MachineGroup(title, shown));
        }
    }
}
