using System;
using Microsoft.AspNetCore.Components;

namespace Exuarch.Web.Workbench
{
    // A tab of the workspace: its title, the problems its badge counts, and what it shows.
    public sealed record WorkspaceTab(string Title, Func<TabBadge> Badge, Func<RenderFragment> Body);

    // A count of problems on a tab, in the colour of their kind: "danger" for errors, "warning" for warnings. A count
    // of 0 shows no badge.
    public readonly record struct TabBadge(int Count, string Kind)
    {
        public static readonly TabBadge None = new TabBadge(0, null);
    }
}
