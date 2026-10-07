using System;
using System.Collections.Generic;
using Exuarch.Core;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Drawer
{
    // What a machine in the list can do, by name: be read when it is the one open, or be loaded, exported, deleted or
    // reset.
    public sealed record PackageActions(EventCallback Read, EventCallback<string> Load, EventCallback<string> Export, EventCallback<string> Delete, EventCallback<string> Reset);

    // Which groups of examples are open. Simple starts open, and so does the group of the machine that is open; after
    // that each stays as the viewer left it.
    public sealed class ExampleFolds
    {
        private readonly Dictionary<string, bool> open = new Dictionary<string, bool>();
        private readonly Func<string> openMachine;

        public ExampleFolds(Func<string> openMachine)
        {
            this.openMachine = openMachine;
        }

        public bool IsOpen(string level)
        {
            return open.TryGetValue(level, out var isOpen) ? isOpen : level == "simple" || BuiltInPackages.Level(openMachine()) == level;
        }

        public void Toggle(string level)
        {
            open[level] = !IsOpen(level);
        }
    }
}
